using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class SellerCatalogueEndpoints
{
    public static IEndpointRouteBuilder MapSellerCatalogueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var seller = endpoints.MapGroup("/api/seller").WithTags("Seller catalogue")
            .RequireAuthorization(AuthorizationPolicies.SellerWorkspace);
        seller.MapGet("/store", GetStoreAsync).Produces<SellerStoreResponse>();
        seller.MapPut("/store", UpdateStoreAsync).Produces<SellerStoreResponse>().ProducesValidationProblem();
        seller.MapGet("/products", GetProductsAsync).Produces<IReadOnlyCollection<SellerProductResponse>>();
        seller.MapPost("/products", CreateProductAsync).Produces<SellerProductResponse>(StatusCodes.Status201Created).ProducesValidationProblem();
        seller.MapPut("/products/{productId:guid}", UpdateProductAsync).Produces<SellerProductResponse>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);
        seller.MapPut("/products/{productId:guid}/inventory", UpdateInventoryAsync).Produces<SellerProductResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict);
        seller.MapPost("/products/{productId:guid}/publish", PublishProductAsync).Produces<SellerProductResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        seller.MapPost("/products/{productId:guid}/unpublish", UnpublishProductAsync).Produces<SellerProductResponse>();
        seller.MapDelete("/products/{productId:guid}", DeleteProductAsync).Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        var admin = endpoints.MapGroup("/api/admin/sellers").WithTags("Seller administration")
            .RequireAuthorization(AuthorizationPolicies.UserAdministration);
        admin.MapGet("/applications", GetApplicationsAsync).Produces<IReadOnlyCollection<SellerApplicationResponse>>();
        admin.MapPost("/{sellerId:guid}/decision", DecideApplicationAsync).Produces<SellerApplicationResponse>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<Store> OwnedStoreAsync(ClaimsPrincipal principal, MarketplaceDbContext db, CancellationToken ct) =>
        await db.Stores.Include(x => x.Seller).SingleAsync(x => x.SellerId == UserId(principal), ct);

    private static async Task<IResult> GetStoreAsync(ClaimsPrincipal principal, MarketplaceDbContext db, CancellationToken ct) =>
        Results.Ok(ToStore(await OwnedStoreAsync(principal, db, ct)));

    private static async Task<IResult> UpdateStoreAsync(SellerStoreUpdateRequest request, ClaimsPrincipal principal,
        MarketplaceDbContext db, HttpContext http, CancellationToken ct)
    {
        var errors = EndpointValidation.Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var store = await OwnedStoreAsync(principal, db, ct);
        store.Name = request.Name.Trim(); store.Description = Clean(request.Description);
        store.SupportEmail = Clean(request.SupportEmail); store.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(db, principal, store.Id, "SellerStoreUpdated", http, new { store.Name, store.Description });
        await db.SaveChangesAsync(ct); return Results.Ok(ToStore(store));
    }

    private static async Task<IResult> GetProductsAsync(ClaimsPrincipal principal, MarketplaceDbContext db, CancellationToken ct)
    {
        var sellerId = UserId(principal);
        var products = await ProductQuery(db).Where(x => x.Store.SellerId == sellerId)
            .OrderByDescending(x => x.UpdatedAt).ToArrayAsync(ct);
        return Results.Ok(products.Select(ToProduct).ToArray());
    }

    private static async Task<IResult> CreateProductAsync(SellerProductRequest request, ClaimsPrincipal principal,
        MarketplaceDbContext db, HttpContext http, CancellationToken ct)
    {
        var store = await OwnedStoreAsync(principal, db, ct);
        var errors = await ValidateProductAsync(request, null, store.Id, db, ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var categories = await db.Categories.Where(x => request.CategoryIds.Contains(x.Id) && x.IsActive).ToArrayAsync(ct);
        var product = new Product
        {
            Store = store,
            Sku = request.Sku.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            Description = Clean(request.Description),
            Price = request.Price,
            Currency = "ZAR",
            Status = ProductStatus.Draft,
            Inventory = new InventoryItem { OnHandQuantity = request.InitialStock, ReorderLevel = request.ReorderLevel, UpdatedAt = DateTimeOffset.UtcNow }
        };
        product.Inventory.Product = product;
        foreach (var category in categories) product.Categories.Add(new ProductCategory { Product = product, Category = category });
        SetImage(product, request.ImageUrl, request.ImageAltText);
        if (request.InitialStock > 0) product.Inventory.Transactions.Add(new InventoryTransaction
        {
            ProductId = product.Id,
            Type = InventoryTransactionType.InitialStock,
            QuantityDelta = request.InitialStock,
            Reason = "Initial reseller stock",
            CreatedByUserId = UserId(principal)
        });
        db.Products.Add(product); Audit(db, principal, product.Id, "SellerProductCreated", http, new { product.Sku, product.Name });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/seller/products/{product.Id}", ToProduct(product));
    }

    private static async Task<IResult> UpdateProductAsync(Guid productId, SellerProductRequest request,
        ClaimsPrincipal principal, MarketplaceDbContext db, HttpContext http, CancellationToken ct)
    {
        var product = await OwnedProductAsync(productId, principal, db, ct);
        if (product is null) return Results.NotFound();
        var errors = await ValidateProductAsync(request, productId, product.StoreId, db, ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        product.Sku = request.Sku.Trim().ToUpperInvariant(); product.Name = request.Name.Trim();
        product.Slug = request.Slug.Trim().ToLowerInvariant(); product.Description = Clean(request.Description);
        product.Price = request.Price; product.UpdatedAt = DateTimeOffset.UtcNow;
        var requestedIds = request.CategoryIds.Distinct().ToHashSet();
        foreach (var link in product.Categories.Where(x => !requestedIds.Contains(x.CategoryId)).ToArray())
            product.Categories.Remove(link);
        var existingIds = product.Categories.Select(x => x.CategoryId).ToHashSet();
        foreach (var category in await db.Categories.Where(x => requestedIds.Contains(x.Id) && x.IsActive).ToArrayAsync(ct))
            if (!existingIds.Contains(category.Id)) product.Categories.Add(new ProductCategory { Product = product, Category = category });
        SetImage(product, request.ImageUrl, request.ImageAltText);
        Audit(db, principal, product.Id, "SellerProductUpdated", http, new { product.Sku, product.Name, product.Price });
        await db.SaveChangesAsync(ct); return Results.Ok(ToProduct(product));
    }

    private static async Task<IResult> UpdateInventoryAsync(Guid productId, SellerInventoryRequest request,
        ClaimsPrincipal principal, MarketplaceDbContext db, HttpContext http, CancellationToken ct)
    {
        var errors = EndpointValidation.Validate(request);
        var product = await OwnedProductAsync(productId, principal, db, ct);
        if (product is null) return Results.NotFound();
        if (request.OnHandQuantity < product.Inventory.ReservedQuantity)
            errors["OnHandQuantity"] = ["On-hand stock cannot be lower than stock reserved by customer orders."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var delta = request.OnHandQuantity - product.Inventory.OnHandQuantity;
        product.Inventory.OnHandQuantity = request.OnHandQuantity; product.Inventory.ReorderLevel = request.ReorderLevel;
        product.Inventory.Version++; product.Inventory.UpdatedAt = DateTimeOffset.UtcNow;
        if (delta != 0) db.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = product.Id,
            Type = InventoryTransactionType.Adjustment,
            QuantityDelta = delta,
            Reason = request.Reason.Trim(),
            CreatedByUserId = UserId(principal)
        });
        Audit(db, principal, product.Id, "SellerInventoryAdjusted", http, new { delta, request.OnHandQuantity, request.ReorderLevel });
        await db.SaveChangesAsync(ct); return Results.Ok(ToProduct(product));
    }

    private static async Task<IResult> PublishProductAsync(Guid productId, ClaimsPrincipal principal,
        MarketplaceDbContext db, HttpContext http, CancellationToken ct)
    {
        var product = await OwnedProductAsync(productId, principal, db, ct);
        if (product is null) return Results.NotFound();
        if (product.Store.Seller.Status != SellerStatus.Approved || product.Store.Status != StoreStatus.Active)
            return Results.Problem("Seller approval and an active store are required before publication.", statusCode: StatusCodes.Status409Conflict);
        if (product.Categories.Count == 0)
            return Results.Problem("At least one active category is required before publication.", statusCode: StatusCodes.Status409Conflict);
        product.Status = ProductStatus.Active; product.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(db, principal, product.Id, "SellerProductPublished", http, new { product.Name });
        await db.SaveChangesAsync(ct); return Results.Ok(ToProduct(product));
    }

    private static async Task<IResult> UnpublishProductAsync(Guid productId, ClaimsPrincipal principal,
        MarketplaceDbContext db, HttpContext http, CancellationToken ct)
    {
        var product = await OwnedProductAsync(productId, principal, db, ct);
        if (product is null) return Results.NotFound();
        product.Status = ProductStatus.Inactive; product.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(db, principal, product.Id, "SellerProductUnpublished", http, new { product.Name });
        await db.SaveChangesAsync(ct); return Results.Ok(ToProduct(product));
    }

    private static async Task<IResult> DeleteProductAsync(Guid productId, ClaimsPrincipal principal,
        MarketplaceDbContext db, HttpContext http, CancellationToken ct)
    {
        var product = await OwnedProductAsync(productId, principal, db, ct);
        if (product is null) return Results.NotFound();
        product.IsDeleted = true; product.Status = ProductStatus.Archived; product.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(db, principal, product.Id, "SellerProductArchived", http, new { product.Name });
        await db.SaveChangesAsync(ct); return Results.NoContent();
    }

    private static async Task<IResult> GetApplicationsAsync(MarketplaceDbContext db, CancellationToken ct)
    {
        var applications = await db.SellerProfiles.AsNoTracking().Include(x => x.User).Include(x => x.Store)
            .OrderBy(x => x.Status).ThenBy(x => x.CreatedAt).ToArrayAsync(ct);
        return Results.Ok(applications.Select(ToApplication).ToArray());
    }

    private static async Task<IResult> DecideApplicationAsync(Guid sellerId, SellerDecisionRequest request,
        ClaimsPrincipal principal, MarketplaceDbContext db, HttpContext http, CancellationToken ct)
    {
        var errors = EndpointValidation.Validate(request); var action = request.Action.Trim().ToLowerInvariant();
        if (action is not ("approve" or "reject" or "suspend")) errors["Action"] = ["Use Approve, Reject, or Suspend."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var seller = await db.SellerProfiles.Include(x => x.User).Include(x => x.Store).SingleOrDefaultAsync(x => x.UserId == sellerId, ct);
        if (seller?.Store is null) return Results.NotFound();
        var now = DateTimeOffset.UtcNow;
        if (action == "approve") { seller.Status = SellerStatus.Approved; seller.ApprovedAt = now; seller.Store.Status = StoreStatus.Active; }
        else if (action == "reject") { seller.Status = SellerStatus.Rejected; seller.ApprovedAt = null; seller.Store.Status = StoreStatus.Closed; }
        else { seller.Status = SellerStatus.Suspended; seller.Store.Status = StoreStatus.Suspended; }
        Audit(db, principal, seller.UserId, $"Seller{request.Action.Trim()}", http,
            new { SellerStatus = seller.Status, StoreStatus = seller.Store.Status });
        await db.SaveChangesAsync(ct); return Results.Ok(ToApplication(seller));
    }

    private static IQueryable<Product> ProductQuery(MarketplaceDbContext db) => db.Products
        .Include(x => x.Store).ThenInclude(x => x.Seller).Include(x => x.Inventory)
        .Include(x => x.Categories).ThenInclude(x => x.Category).Include(x => x.Images);
    private static async Task<Product?> OwnedProductAsync(Guid id, ClaimsPrincipal principal, MarketplaceDbContext db, CancellationToken ct) =>
        await ProductQuery(db).SingleOrDefaultAsync(x => x.Id == id && x.Store.SellerId == UserId(principal), ct);

    private static async Task<Dictionary<string, string[]>> ValidateProductAsync(SellerProductRequest request, Guid? productId,
        Guid storeId, MarketplaceDbContext db, CancellationToken ct)
    {
        var errors = EndpointValidation.Validate(request);
        if (request.CategoryIds.Count == 0) errors["CategoryIds"] = ["Choose at least one active category."];
        else if (await db.Categories.CountAsync(x => request.CategoryIds.Contains(x.Id) && x.IsActive, ct) != request.CategoryIds.Distinct().Count())
            errors["CategoryIds"] = ["One or more categories are unavailable."];
        Uri? uri = null;
        if (!string.IsNullOrWhiteSpace(request.ImageUrl) &&
            (!Uri.TryCreate(request.ImageUrl, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps))
            errors["ImageUrl"] = ["Product images must use a public HTTPS URL."];
        if (!string.IsNullOrWhiteSpace(request.ImageUrl) && string.IsNullOrWhiteSpace(request.ImageAltText))
            errors["ImageAltText"] = ["Describe the product image for customers using assistive technology."];
        var sku = request.Sku.Trim().ToUpperInvariant(); var slug = request.Slug.Trim().ToLowerInvariant();
        if (await db.Products.IgnoreQueryFilters().AnyAsync(x => x.Sku == sku && x.Id != productId, ct)) errors["Sku"] = ["This SKU is already in use."];
        if (await db.Products.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug && x.Id != productId && x.StoreId == storeId, ct))
            errors["Slug"] = ["This product address is already in use for the store."];
        return errors;
    }

    private static void SetImage(Product product, string? url, string? alt)
    {
        var primary = product.Images.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SortOrder).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url)) { product.Images.Clear(); return; }
        if (primary is null)
        {
            primary = new ProductImage { Product = product, StorageKey = $"external/{product.Id:N}/primary" };
            product.Images.Add(primary);
        }
        primary.PublicUrl = url.Trim(); primary.AltText = alt!.Trim(); primary.SortOrder = 0; primary.IsPrimary = true;
        foreach (var extra in product.Images.Where(x => x != primary).ToArray()) product.Images.Remove(extra);
    }
    private static SellerStoreResponse ToStore(Store store) => new(store.Id, store.Name, store.Slug, store.Description,
        store.SupportEmail, store.Status.ToString(), store.Seller.Status.ToString(),
        store.Status == StoreStatus.Active && store.Seller.Status == SellerStatus.Approved);
    private static SellerProductResponse ToProduct(Product p) => new(p.Id, p.Sku, p.Name, p.Slug, p.Description, p.Price,
        p.Currency, p.Status.ToString(), p.Inventory.OnHandQuantity, p.Inventory.ReservedQuantity,
        p.Inventory.OnHandQuantity - p.Inventory.ReservedQuantity, p.Inventory.ReorderLevel,
        p.Categories.OrderBy(x => x.Category.Name).Select(x => new ProductCategoryResponse(x.Category.Id, x.Category.Name, x.Category.Slug)).ToArray(),
        p.Images.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SortOrder).Select(x => x.PublicUrl).FirstOrDefault(),
        p.Images.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SortOrder).Select(x => x.AltText).FirstOrDefault(), p.UpdatedAt);
    private static SellerApplicationResponse ToApplication(SellerProfile s) => new(s.UserId, s.User.DisplayName, s.User.Email!,
        s.TradingName, s.Status.ToString(), s.Store!.Name, s.Store.Slug, s.Store.Status.ToString(), s.CreatedAt);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Audit(MarketplaceDbContext db, ClaimsPrincipal principal, Guid id, string action, HttpContext http, object changes) =>
        db.AuditEntries.Add(new AuditEntry
        {
            UserId = UserId(principal),
            EntityType = "SellerCatalogue",
            EntityId = id.ToString(),
            Action = action,
            ChangesJson = JsonSerializer.Serialize(changes),
            CorrelationId = http.TraceIdentifier,
            OccurredAt = DateTimeOffset.UtcNow
        });
}

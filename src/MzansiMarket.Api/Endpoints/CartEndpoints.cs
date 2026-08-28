using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cart")
            .WithTags("Shopping cart")
            .RequireAuthorization(AuthorizationPolicies.CustomerAccess);

        group.MapGet("/", GetCartAsync).Produces<CartResponse>();
        group.MapPost("/items", AddItemAsync)
            .Produces<CartResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/items/{id:guid}", UpdateItemAsync)
            .Produces<CartResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/items/{id:guid}", RemoveItemAsync)
            .Produces<CartResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetCartAsync(
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var cart = await LoadCartAsync(dbContext, GetUserId(principal), tracked: false, cancellationToken);
        return Results.Ok(ToResponse(cart));
    }

    private static async Task<IResult> AddItemAsync(
        AddCartItemRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (request.ProductId == Guid.Empty) errors["ProductId"] = ["Product ID is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var product = await LoadPurchasableProductAsync(dbContext, request.ProductId, cancellationToken);
        if (product is null) return Results.NotFound();

        var userId = GetUserId(principal);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var cart = await LoadCartAsync(dbContext, userId, tracked: true, cancellationToken);
        if (cart is null)
        {
            cart = new Cart { CustomerId = userId, Status = CartStatus.Active };
            dbContext.Carts.Add(cart);
        }

        var existing = cart.Items.SingleOrDefault(item => item.ProductId == request.ProductId);
        var requestedQuantity = request.Quantity + (existing?.Quantity ?? 0);
        var availabilityProblem = ValidateAvailability(product, requestedQuantity);
        if (availabilityProblem is not null) return availabilityProblem;

        if (existing is null)
        {
            var item = new CartItem { ProductId = product.Id, Product = product, Quantity = request.Quantity };
            cart.Items.Add(item);
            dbContext.CartItems.Add(item);
        }
        else
        {
            existing.Quantity = requestedQuantity;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Results.Created("/api/cart", ToResponse(cart));
    }

    private static async Task<IResult> UpdateItemAsync(
        Guid id,
        UpdateCartItemRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var cart = await LoadCartAsync(dbContext, GetUserId(principal), tracked: true, cancellationToken);
        var item = cart?.Items.SingleOrDefault(cartItem => cartItem.Id == id);
        if (item is null) return Results.NotFound();

        var product = await LoadPurchasableProductAsync(dbContext, item.ProductId, cancellationToken);
        if (product is null)
        {
            return Results.Problem(
                "This product is no longer available.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var availabilityProblem = ValidateAvailability(product, request.Quantity);
        if (availabilityProblem is not null) return availabilityProblem;

        item.Quantity = request.Quantity;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(cart));
    }

    private static async Task<IResult> RemoveItemAsync(
        Guid id,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var cart = await LoadCartAsync(dbContext, GetUserId(principal), tracked: true, cancellationToken);
        var item = cart?.Items.SingleOrDefault(cartItem => cartItem.Id == id);
        if (item is null) return Results.NotFound();

        cart!.Items.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(cart));
    }

    private static async Task<Cart?> LoadCartAsync(
        MarketplaceDbContext dbContext,
        Guid userId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Carts.IgnoreQueryFilters()
            .Include(cart => cart.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Store)
            .Include(cart => cart.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Inventory)
            .Include(cart => cart.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Categories)
                .ThenInclude(link => link.Category)
            .Include(cart => cart.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Images)
            .Where(cart => cart.CustomerId == userId && cart.Status == CartStatus.Active);
        if (!tracked) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<Product?> LoadPurchasableProductAsync(
        MarketplaceDbContext dbContext,
        Guid productId,
        CancellationToken cancellationToken) =>
        await dbContext.Products
            .Include(product => product.Store)
            .Include(product => product.Inventory)
            .Include(product => product.Categories).ThenInclude(link => link.Category)
            .Include(product => product.Images)
            .SingleOrDefaultAsync(product =>
                product.Id == productId
                && product.Status == ProductStatus.Active
                && product.Store.Status == StoreStatus.Active
                && product.Categories.Any(link => link.Category.IsActive),
                cancellationToken);

    private static IResult? ValidateAvailability(Product product, int requestedQuantity)
    {
        var availableQuantity = product.Inventory.OnHandQuantity - product.Inventory.ReservedQuantity;
        return requestedQuantity <= availableQuantity
            ? null
            : Results.Problem(
                title: "Insufficient stock",
                detail: $"Only {availableQuantity} item(s) are currently available.",
                statusCode: StatusCodes.Status409Conflict);
    }

    private static CartResponse ToResponse(Cart? cart)
    {
        if (cart is null) return new CartResponse(null, [], 0, 0m, "ZAR");

        var items = cart.Items
            .OrderBy(item => item.CreatedAt)
            .Select(item =>
            {
                var product = item.Product;
                var availableQuantity = product.Inventory.OnHandQuantity - product.Inventory.ReservedQuantity;
                var image = product.Images.OrderByDescending(candidate => candidate.IsPrimary)
                    .ThenBy(candidate => candidate.SortOrder)
                    .FirstOrDefault();
                var isAvailable = !product.IsDeleted
                    && product.Status == ProductStatus.Active
                    && product.Store.Status == StoreStatus.Active
                    && product.Categories.Any(link => link.Category.IsActive)
                    && item.Quantity <= availableQuantity;
                return new CartItemResponse(
                    item.Id,
                    product.Id,
                    product.Name,
                    product.Slug,
                    product.Store.Name,
                    product.Store.Slug,
                    item.Quantity,
                    product.Price,
                    product.Price * item.Quantity,
                    availableQuantity,
                    isAvailable,
                    image?.PublicUrl,
                    image?.AltText);
            })
            .ToArray();

        return new CartResponse(
            cart.Id,
            items,
            items.Sum(item => item.Quantity),
            items.Sum(item => item.LineTotal),
            "ZAR");
    }

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class CatalogueEndpoints
{
    private static readonly HashSet<string> AllowedSorts =
        new(["newest", "name", "price-asc", "price-desc"], StringComparer.OrdinalIgnoreCase);

    public static IEndpointRouteBuilder MapCatalogueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Catalogue").AllowAnonymous();

        group.MapGet("/categories", GetCategoriesAsync)
            .Produces<IReadOnlyCollection<CategorySummaryResponse>>();
        group.MapGet("/products", GetProductsAsync)
            .Produces<PagedResponse<ProductSummaryResponse>>()
            .ProducesValidationProblem();
        group.MapGet("/products/{id:guid}", GetProductByIdAsync)
            .Produces<ProductDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/stores/{storeSlug}/products/{productSlug}", GetProductBySlugAsync)
            .Produces<ProductDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetCategoriesAsync(
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories.AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.Name)
            .Select(category => new CategorySummaryResponse(
                category.Id,
                category.Name,
                category.Slug,
                category.ParentCategoryId,
                category.Products.Count(link =>
                    link.Product.Status == ProductStatus.Active
                    && link.Product.Store.Status == StoreStatus.Active)))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(categories);
    }

    private static async Task<IResult> GetProductsAsync(
        [AsParameters] ProductQuery request,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? 24;
        var products = ActiveProducts(dbContext);
        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            if (dbContext.Database.IsNpgsql())
            {
                var pattern = $"%{search}%";
                products = products.Where(product =>
                    EF.Functions.ILike(product.Name, pattern)
                    || EF.Functions.ILike(product.Sku, pattern)
                    || product.Description != null && EF.Functions.ILike(product.Description, pattern)
                    || EF.Functions.ILike(product.Store.Name, pattern));
            }
            else
            {
                var normalized = search.ToLowerInvariant();
                products = products.Where(product =>
                    product.Name.ToLower().Contains(normalized)
                    || product.Sku.ToLower().Contains(normalized)
                    || product.Description != null && product.Description.ToLower().Contains(normalized)
                    || product.Store.Name.ToLower().Contains(normalized));
            }
        }

        var category = request.Category?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(category))
        {
            products = products.Where(product => product.Categories.Any(link =>
                link.Category.IsActive && link.Category.Slug == category));
        }

        var store = request.Store?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(store))
        {
            products = products.Where(product => product.Store.Slug == store);
        }

        if (request.MinimumPrice is { } minimumPrice)
        {
            products = products.Where(product => product.Price >= minimumPrice);
        }

        if (request.MaximumPrice is { } maximumPrice)
        {
            products = products.Where(product => product.Price <= maximumPrice);
        }

        if (request.InStock is { } inStock)
        {
            products = inStock
                ? products.Where(product => product.Inventory.OnHandQuantity > product.Inventory.ReservedQuantity)
                : products.Where(product => product.Inventory.OnHandQuantity <= product.Inventory.ReservedQuantity);
        }

        products = (request.Sort ?? "newest").ToLowerInvariant() switch
        {
            "name" => products.OrderBy(product => product.Name).ThenBy(product => product.Id),
            "price-asc" => products.OrderBy(product => product.Price).ThenBy(product => product.Name),
            "price-desc" => products.OrderByDescending(product => product.Price).ThenBy(product => product.Name),
            _ => products.OrderByDescending(product => product.CreatedAt).ThenBy(product => product.Id)
        };

        var totalCount = await products.CountAsync(cancellationToken);
        var items = await products
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductSummaryResponse(
                product.Id,
                product.Sku,
                product.Name,
                product.Slug,
                product.Price,
                product.Currency,
                product.Inventory.OnHandQuantity - product.Inventory.ReservedQuantity,
                product.Inventory.OnHandQuantity > product.Inventory.ReservedQuantity,
                product.Store.Name,
                product.Store.Slug,
                product.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder)
                    .Select(image => image.PublicUrl).FirstOrDefault(),
                product.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder)
                    .Select(image => image.AltText).FirstOrDefault()))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new PagedResponse<ProductSummaryResponse>(
            items,
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    private static async Task<IResult> GetProductByIdAsync(
        Guid id,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var product = await ProjectDetails(ActiveProducts(dbContext).Where(item => item.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
        return product is null ? Results.NotFound() : Results.Ok(product);
    }

    private static async Task<IResult> GetProductBySlugAsync(
        string storeSlug,
        string productSlug,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var normalizedStoreSlug = storeSlug.Trim().ToLowerInvariant();
        var normalizedProductSlug = productSlug.Trim().ToLowerInvariant();
        var product = await ProjectDetails(ActiveProducts(dbContext).Where(item =>
                item.Store.Slug == normalizedStoreSlug && item.Slug == normalizedProductSlug))
            .SingleOrDefaultAsync(cancellationToken);
        return product is null ? Results.NotFound() : Results.Ok(product);
    }

    private static IQueryable<Product> ActiveProducts(MarketplaceDbContext dbContext) =>
        dbContext.Products.AsNoTracking().Where(product =>
            product.Status == ProductStatus.Active
            && product.Store.Status == StoreStatus.Active
            && product.Categories.Any(link => link.Category.IsActive));

    private static IQueryable<ProductDetailResponse> ProjectDetails(IQueryable<Product> products) =>
        products.Select(product => new ProductDetailResponse(
            product.Id,
            product.Sku,
            product.Name,
            product.Slug,
            product.Description,
            product.Price,
            product.Currency,
            product.Inventory.OnHandQuantity - product.Inventory.ReservedQuantity,
            product.Inventory.OnHandQuantity > product.Inventory.ReservedQuantity,
            new ProductStoreResponse(product.Store.Id, product.Store.Name, product.Store.Slug, product.Store.Description),
            product.Categories.Where(link => link.Category.IsActive)
                .OrderBy(link => link.Category.Name)
                .Select(link => new ProductCategoryResponse(link.Category.Id, link.Category.Name, link.Category.Slug))
                .ToArray(),
            product.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder)
                .Select(image => new ProductImageResponse(
                    image.PublicUrl,
                    image.AltText,
                    image.SortOrder,
                    image.IsPrimary))
                .ToArray()));

    private static Dictionary<string, string[]> Validate(ProductQuery request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (request.Page is < 1) errors["Page"] = ["Page must be at least 1."];
        if (request.PageSize is < 1 or > 100) errors["PageSize"] = ["Page size must be between 1 and 100."];
        if (request.MinimumPrice < 0) errors["MinimumPrice"] = ["Minimum price cannot be negative."];
        if (request.MaximumPrice < 0) errors["MaximumPrice"] = ["Maximum price cannot be negative."];
        if (request.MinimumPrice > request.MaximumPrice)
        {
            errors["PriceRange"] = ["Minimum price cannot exceed maximum price."];
        }

        if (request.Search?.Length > 120) errors["Search"] = ["Search cannot exceed 120 characters."];
        if (request.Category?.Length > 140) errors["Category"] = ["Category cannot exceed 140 characters."];
        if (request.Store?.Length > 180) errors["Store"] = ["Store cannot exceed 180 characters."];
        if (request.Sort is not null && !AllowedSorts.Contains(request.Sort))
        {
            errors["Sort"] = ["Sort must be newest, name, price-asc, or price-desc."];
        }

        return errors;
    }
}

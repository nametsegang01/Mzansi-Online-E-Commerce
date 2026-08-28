namespace MzansiMarket.Api.Contracts;

public sealed class ProductQuery
{
    public string? Search { get; init; }
    public string? Category { get; init; }
    public string? Store { get; init; }
    public decimal? MinimumPrice { get; init; }
    public decimal? MaximumPrice { get; init; }
    public bool? InStock { get; init; }
    public string? Sort { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

public sealed record PagedResponse<T>(
    IReadOnlyCollection<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CategorySummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentCategoryId,
    int ActiveProductCount);

public sealed record ProductSummaryResponse(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    decimal Price,
    string Currency,
    int AvailableQuantity,
    bool IsInStock,
    string StoreName,
    string StoreSlug,
    string? PrimaryImageUrl,
    string? PrimaryImageAltText);

public sealed record ProductDetailResponse(
    Guid Id,
    string Sku,
    string Name,
    string Slug,
    string? Description,
    decimal Price,
    string Currency,
    int AvailableQuantity,
    bool IsInStock,
    ProductStoreResponse Store,
    IReadOnlyCollection<ProductCategoryResponse> Categories,
    IReadOnlyCollection<ProductImageResponse> Images);

public sealed record ProductStoreResponse(Guid Id, string Name, string Slug, string? Description);

public sealed record ProductCategoryResponse(Guid Id, string Name, string Slug);

public sealed record ProductImageResponse(string Url, string AltText, int SortOrder, bool IsPrimary);

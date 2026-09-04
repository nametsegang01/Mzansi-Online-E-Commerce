using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class SellerStoreUpdateRequest
{
    [Required, StringLength(180, MinimumLength = 2)] public string Name { get; init; } = string.Empty;
    [StringLength(2000)] public string? Description { get; init; }
    [EmailAddress, StringLength(256)] public string? SupportEmail { get; init; }
}

public sealed class SellerProductRequest
{
    [Required, RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]*$"), StringLength(80, MinimumLength = 2)]
    public string Sku { get; init; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 2)] public string Name { get; init; } = string.Empty;
    [Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$"), StringLength(220, MinimumLength = 2)]
    public string Slug { get; init; } = string.Empty;
    [StringLength(4000)] public string? Description { get; init; }
    [Range(0.01, 9_999_999_999_999_999d)] public decimal Price { get; init; }
    public IReadOnlyCollection<Guid> CategoryIds { get; init; } = [];
    [Url, StringLength(1000)] public string? ImageUrl { get; init; }
    [StringLength(300)] public string? ImageAltText { get; init; }
    [Range(0, 1_000_000)] public int InitialStock { get; init; }
    [Range(0, 1_000_000)] public int ReorderLevel { get; init; }
}

public sealed class SellerInventoryRequest
{
    [Range(0, 1_000_000)] public int OnHandQuantity { get; init; }
    [Range(0, 1_000_000)] public int ReorderLevel { get; init; }
    [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = string.Empty;
}

public sealed class SellerDecisionRequest
{
    [Required, StringLength(24)] public string Action { get; init; } = string.Empty;
}

public sealed record SellerStoreResponse(Guid Id, string Name, string Slug, string? Description,
    string? SupportEmail, string StoreStatus, string SellerStatus, bool CanPublish);

public sealed record SellerProductResponse(Guid Id, string Sku, string Name, string Slug, string? Description,
    decimal Price, string Currency, string Status, int OnHandQuantity, int ReservedQuantity,
    int AvailableQuantity, int ReorderLevel, IReadOnlyCollection<ProductCategoryResponse> Categories,
    string? ImageUrl, string? ImageAltText, DateTimeOffset UpdatedAt);

public sealed record SellerApplicationResponse(Guid SellerId, string DisplayName, string Email,
    string TradingName, string SellerStatus, string StoreName, string StoreSlug, string StoreStatus,
    DateTimeOffset CreatedAt);

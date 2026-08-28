using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class AddCartItemRequest
{
    public Guid ProductId { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }
}

public sealed class UpdateCartItemRequest
{
    [Range(1, 100)]
    public int Quantity { get; init; }
}

public sealed record CartResponse(
    Guid? CartId,
    IReadOnlyCollection<CartItemResponse> Items,
    int ItemCount,
    decimal Subtotal,
    string Currency);

public sealed record CartItemResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string StoreName,
    string StoreSlug,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int AvailableQuantity,
    bool IsAvailable,
    string? ImageUrl,
    string? ImageAltText);

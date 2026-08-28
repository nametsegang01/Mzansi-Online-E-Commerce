using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class CheckoutRequest
{
    public Guid AddressId { get; init; }

    [StringLength(80)]
    public string? PromotionCode { get; init; }
}

public sealed record CheckoutResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal DeliveryTotal,
    decimal GrandTotal,
    string Currency,
    DateTimeOffset ReservationExpiresAt,
    string? PromotionCode,
    CheckoutAddressResponse ShippingAddress,
    IReadOnlyCollection<CheckoutSellerOrderResponse> SellerOrders);

public sealed record CheckoutAddressResponse(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string Province,
    string PostalCode,
    string CountryCode);

public sealed record CheckoutSellerOrderResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal DeliveryTotal,
    decimal GrandTotal,
    IReadOnlyCollection<CheckoutItemResponse> Items);

public sealed record CheckoutItemResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal);

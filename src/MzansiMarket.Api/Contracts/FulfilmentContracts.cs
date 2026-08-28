using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class FulfilmentQuery
{
    [StringLength(40)]
    public string? Status { get; init; }
}

public sealed class FulfilmentTransitionRequest
{
    [Required, StringLength(40)]
    public string Action { get; init; } = string.Empty;

    [StringLength(100)]
    public string? Carrier { get; init; }

    [StringLength(160)]
    public string? TrackingNumber { get; init; }
}

public sealed record FulfilmentOrderResponse(
    Guid SellerOrderId,
    Guid OrderId,
    string OrderNumber,
    Guid StoreId,
    string StoreName,
    string Status,
    DateTimeOffset PaidAt,
    string RecipientName,
    string City,
    string Province,
    IReadOnlyCollection<FulfilmentItemResponse> Items,
    FulfilmentShipmentResponse? Shipment);

public sealed record FulfilmentItemResponse(
    Guid OrderItemId,
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity);

public sealed record FulfilmentShipmentResponse(
    Guid ShipmentId,
    string Status,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt);

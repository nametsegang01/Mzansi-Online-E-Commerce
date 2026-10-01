namespace MzansiMarket.Api.Contracts;

public sealed record CustomerOrderResponse(
    string OrderNumber,
    DateTimeOffset PlacedAt,
    string Status,
    decimal Total,
    string Currency,
    IReadOnlyCollection<CustomerOrderItemResponse> Items,
    IReadOnlyCollection<CustomerSellerOrderResponse> SellerOrders);

public sealed record CustomerOrderItemResponse(string Name, int Quantity);

public sealed record CustomerSellerOrderResponse(
    string Status,
    CustomerShipmentResponse? Shipment);

public sealed record CustomerShipmentResponse(
    string Status,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt);
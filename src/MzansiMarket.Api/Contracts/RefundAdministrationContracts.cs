using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

/// <summary>
/// Filters by RequestedAt in UTC, inclusive. StoreId is matched through
/// ReturnRequest.OrderItem -> SellerOrder -> Store.
/// </summary>
public sealed class RefundQueueQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public Guid? StoreId { get; init; }

    [StringLength(32)]
    public string? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }
}

public sealed class RefundDecisionRequest
{
    [Required, StringLength(16)]
    public string Action { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Reason { get; init; }
}

public sealed record RefundQueueItemResponse(
    Guid ReturnRequestId,
    Guid OrderId,
    string OrderNumber,
    Guid SellerOrderId,
    Guid StoreId,
    string StoreName,
    Guid OrderItemId,
    Guid ProductId,
    string Sku,
    string ProductName,
    int ItemQuantity,
    int ReturnQuantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal ItemLineTotal,
    decimal RefundAmount,
    decimal? MaximumRefundAmount,
    string Currency,
    string CustomerDisplayName,
    string CustomerEmail,
    string Reason,
    string? Details,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyCollection<RefundRecordSummary> Refunds);

public sealed record RefundRecordSummary(
    Guid RefundRecordId,
    decimal Amount,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record RefundDecisionResponse(
    Guid ReturnRequestId,
    string Status,
    DateTimeOffset ResolvedAt,
    decimal? MaximumRefundAmount,
    RefundRecordSummary? RefundRecord);
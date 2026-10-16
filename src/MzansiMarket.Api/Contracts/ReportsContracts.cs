using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

/// <summary>
/// From and To are UTC timestamps and inclusive. Status filters Order.Status.
/// Paid order totals use PaidAt; refund metrics instead use approved returns' RequestedAt.
/// </summary>
public sealed class SalesReportQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public Guid? StoreId { get; init; }

    [StringLength(40)]
    public string? Status { get; init; }
}

/// <summary>
/// A currency-homogeneous paid sales result. RefundCount/RefundValue use approved return
/// requests in the requested period; RefundRate uses distinct matching paid orders with
/// any approved return, divided by paid orders in the paid-time period.
/// RefundRate is a ratio from 0 through 1.
/// </summary>
public sealed record SalesReportResponse(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? StoreId,
    string? Status,
    string Currency,
    decimal TotalSales,
    int OrderCount,
    int RefundCount,
    decimal RefundValue,
    int PaidOrdersWithApprovedReturn,
    decimal RefundRate);

/// <summary>Status filters Product.Status. This is a current stock snapshot.</summary>
public sealed class StockReportQuery
{
    public Guid? StoreId { get; init; }

    [StringLength(32)]
    public string? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }
}

public sealed record StockReportProductResponse(
    Guid ProductId,
    Guid StoreId,
    string StoreName,
    string Sku,
    string Name,
    string Status,
    int OnHandQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int ReorderLevel,
    DateTimeOffset UpdatedAt);

public sealed record StockReportResponse(
    DateTimeOffset GeneratedAt,
    Guid? StoreId,
    string? Status,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyCollection<StockReportProductResponse> Items);

/// <summary>
/// From and To are UTC timestamps and inclusive, matched against shipment DeliveredAt.
/// Status filters SellerOrder.Status. Turnaround averages delivered timestamps only.
/// </summary>
public sealed class ServicePerformanceReportQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public Guid? StoreId { get; init; }

    [StringLength(40)]
    public string? Status { get; init; }
}

public sealed record ServicePerformanceReportResponse(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? StoreId,
    string? Status,
    int DeliveredShipmentCount,
    double? AverageFulfilmentTurnaroundHours);
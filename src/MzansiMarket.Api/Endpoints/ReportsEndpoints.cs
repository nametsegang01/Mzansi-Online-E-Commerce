using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class ReportsEndpoints
{
    private static readonly ReturnStatus[] ApprovedReturnStatuses =
    [
        ReturnStatus.Approved,
        ReturnStatus.Received,
        ReturnStatus.Refunded
    ];

    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/reports")
            .WithTags("Management reports")
            .RequireAuthorization(AuthorizationPolicies.ReportsAccess);

        group.MapGet("/sales", GetSalesAsync)
            .Produces<SalesReportResponse>()
            .ProducesValidationProblem();
        group.MapGet("/stock", GetStockAsync)
            .Produces<StockReportResponse>()
            .ProducesValidationProblem();
        group.MapGet("/service-performance", GetServicePerformanceAsync)
            .Produces<ServicePerformanceReportResponse>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetSalesAsync(
        [AsParameters] SalesReportQuery query,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateDateRange(query.From, query.To);
        var status = ParseStatus<OrderStatus>(query.Status, "Status", errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var paidOrders = dbContext.Orders.AsNoTracking().Where(order => order.PaidAt != null);
        if (query.From is { } from) paidOrders = paidOrders.Where(order => order.PaidAt >= from);
        if (query.To is { } to) paidOrders = paidOrders.Where(order => order.PaidAt <= to);
        if (status is { } orderStatus) paidOrders = paidOrders.Where(order => order.Status == orderStatus);
        if (query.StoreId is { } storeId)
            paidOrders = paidOrders.Where(order => order.SellerOrders.Any(sellerOrder => sellerOrder.StoreId == storeId));

        var currencies = await paidOrders.Select(order => order.Currency).Distinct().Take(2)
            .ToArrayAsync(cancellationToken);
        if (currencies.Length > 1)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Currency"] = ["Sales cannot be combined across multiple currencies; narrow the filters to one currency."]
            });
        }

        decimal totalSales;
        int orderCount;
        if (query.StoreId is { } selectedStoreId)
        {
            var sellerTotals = await paidOrders
                .SelectMany(order => order.SellerOrders.Where(sellerOrder => sellerOrder.StoreId == selectedStoreId)
                    .Select(sellerOrder => new
                    {
                        order.Id,
                        Total = sellerOrder.Subtotal - sellerOrder.DiscountTotal + sellerOrder.DeliveryTotal
                    }))
                .ToArrayAsync(cancellationToken);
            totalSales = sellerTotals.Sum(item => item.Total);
            orderCount = sellerTotals.Select(item => item.Id).Distinct().Count();
        }
        else
        {
            var orderTotals = await paidOrders
                .Select(order => new { order.Id, order.GrandTotal })
                .ToArrayAsync(cancellationToken);
            totalSales = orderTotals.Sum(item => item.GrandTotal);
            orderCount = orderTotals.Length;
        }

        var approvedReturns = dbContext.ReturnRequests.AsNoTracking()
            .Where(item => ApprovedReturnStatuses.Contains(item.Status));
        if (query.From is { } returnFrom) approvedReturns = approvedReturns.Where(item => item.RequestedAt >= returnFrom);
        if (query.To is { } returnTo) approvedReturns = approvedReturns.Where(item => item.RequestedAt <= returnTo);
        if (query.StoreId is { } refundStoreId)
            approvedReturns = approvedReturns.Where(item => item.OrderItem.SellerOrder.StoreId == refundStoreId);
        if (status is { } returnOrderStatus)
            approvedReturns = approvedReturns.Where(item => item.OrderItem.SellerOrder.Order.Status == returnOrderStatus);
        var refundCount = await approvedReturns.CountAsync(cancellationToken);
        var refundValue = await approvedReturns.Select(item => (decimal?)item.RefundAmount)
            .SumAsync(cancellationToken) ?? 0m;

        var paidOrdersWithApprovedReturn = await paidOrders.CountAsync(order =>
            order.SellerOrders.Any(sellerOrder =>
                (!query.StoreId.HasValue || sellerOrder.StoreId == query.StoreId.Value)
                && sellerOrder.Items.Any(orderItem => orderItem.ReturnRequests
                    .Any(returnRequest => ApprovedReturnStatuses.Contains(returnRequest.Status)))),
            cancellationToken);
        var refundRate = orderCount == 0
            ? 0m
            : Math.Min(1m, paidOrdersWithApprovedReturn / (decimal)orderCount);

        return Results.Ok(new SalesReportResponse(
            query.From,
            query.To,
            query.StoreId,
            query.Status,
            currencies.FirstOrDefault() ?? "ZAR",
            totalSales,
            orderCount,
            refundCount,
            refundValue,
            paidOrdersWithApprovedReturn,
            refundRate));
    }

    private static async Task<IResult> GetStockAsync(
        [AsParameters] StockReportQuery query,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var status = ParseStatus<ProductStatus>(query.Status, "Status", errors);
        if (query.Page is < 1) errors["Page"] = ["Page must be at least 1."];
        if (query.PageSize is < 1 or > 100) errors["PageSize"] = ["Page size must be between 1 and 100."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 50;

        var products = dbContext.Products.AsNoTracking()
            .Where(product => product.Inventory.OnHandQuantity - product.Inventory.ReservedQuantity
                <= product.Inventory.ReorderLevel);
        if (query.StoreId is { } storeId) products = products.Where(product => product.StoreId == storeId);
        if (status is { } productStatus) products = products.Where(product => product.Status == productStatus);

        var totalCount = await products.CountAsync(cancellationToken);
        var items = await products.OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new StockReportProductResponse(
                product.Id,
                product.StoreId,
                product.Store.Name,
                product.Sku,
                product.Name,
                product.Status.ToString(),
                product.Inventory.OnHandQuantity,
                product.Inventory.ReservedQuantity,
                product.Inventory.OnHandQuantity - product.Inventory.ReservedQuantity,
                product.Inventory.ReorderLevel,
                product.Inventory.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new StockReportResponse(
            DateTimeOffset.UtcNow,
            query.StoreId,
            query.Status,
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            items));
    }

    private static async Task<IResult> GetServicePerformanceAsync(
        [AsParameters] ServicePerformanceReportQuery query,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateDateRange(query.From, query.To);
        var status = ParseStatus<SellerOrderStatus>(query.Status, "Status", errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var shipments = dbContext.Shipments.AsNoTracking()
            .Where(shipment => shipment.Status == ShipmentStatus.Delivered
                && shipment.DispatchedAt.HasValue
                && shipment.DeliveredAt.HasValue
                && shipment.DeliveredAt >= shipment.DispatchedAt);
        if (query.From is { } from) shipments = shipments.Where(shipment => shipment.DeliveredAt >= from);
        if (query.To is { } to) shipments = shipments.Where(shipment => shipment.DeliveredAt <= to);
        if (query.StoreId is { } storeId)
            shipments = shipments.Where(shipment => shipment.SellerOrder.StoreId == storeId);
        if (status is { } sellerOrderStatus)
            shipments = shipments.Where(shipment => shipment.SellerOrder.Status == sellerOrderStatus);

        var durations = shipments.Select(shipment =>
            (shipment.DeliveredAt!.Value - shipment.DispatchedAt!.Value).TotalHours);
        var deliveredCount = await durations.CountAsync(cancellationToken);
        double? averageHours = deliveredCount == 0
            ? null
            : await durations.AverageAsync(cancellationToken);

        return Results.Ok(new ServicePerformanceReportResponse(
            query.From,
            query.To,
            query.StoreId,
            query.Status,
            deliveredCount,
            averageHours));
    }

    private static Dictionary<string, string[]> ValidateDateRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (from is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero)
            errors["From"] = ["From must be a UTC timestamp."];
        if (to is { Offset: var toOffset } && toOffset != TimeSpan.Zero)
            errors["To"] = ["To must be a UTC timestamp."];
        if (from is { } start && to is { } end)
        {
            if (start > end) errors["DateRange"] = ["From must be earlier than or equal to To."];
            else if (end - start > TimeSpan.FromDays(366))
                errors["DateRange"] = ["The date range cannot exceed 366 days."];
        }
        return errors;
    }

    private static TEnum? ParseStatus<TEnum>(
        string? value,
        string field,
        Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out TEnum parsed))
        {
            return parsed;
        }
        errors[field] = [$"{field} must be an exact PascalCase {typeof(TEnum).Name} value."];
        return null;
    }
}
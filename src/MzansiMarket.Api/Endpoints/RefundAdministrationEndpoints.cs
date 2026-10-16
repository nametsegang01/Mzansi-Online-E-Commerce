using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;
using Npgsql;

namespace MzansiMarket.Api.Endpoints;

public static class RefundAdministrationEndpoints
{
    private static readonly RefundStatus[] CapCountingStatuses =
    [
        RefundStatus.Pending,
        RefundStatus.Submitted,
        RefundStatus.Paid
    ];

    public static IEndpointRouteBuilder MapRefundAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/refunds")
            .WithTags("Refund administration")
            .RequireAuthorization(AuthorizationPolicies.RefundAdministration);

        group.MapGet("/", GetQueueAsync)
            .Produces<PagedResponse<RefundQueueItemResponse>>()
            .ProducesValidationProblem();
        group.MapPost("/{returnRequestId:guid}/decision", DecideAsync)
            .Produces<RefundDecisionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetQueueAsync(
        [AsParameters] RefundQueueQuery query,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateQuery(query);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? 50;

        var requests = dbContext.ReturnRequests.AsNoTracking().AsQueryable();
        if (query.From is { } from) requests = requests.Where(item => item.RequestedAt >= from);
        if (query.To is { } to) requests = requests.Where(item => item.RequestedAt <= to);
        if (query.StoreId is { } storeId)
            requests = requests.Where(item => item.OrderItem.SellerOrder.StoreId == storeId);
        if (!string.IsNullOrWhiteSpace(query.Status))
            requests = requests.Where(item => item.Status.ToString() == query.Status);

        var totalCount = await requests.CountAsync(cancellationToken);
        var pageItems = await requests
            .OrderBy(item => item.RequestedAt)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(item => item.Customer)
            .Include(item => item.Refunds)
            .Include(item => item.OrderItem).ThenInclude(item => item.SellerOrder).ThenInclude(order => order.Store)
            .Include(item => item.OrderItem).ThenInclude(item => item.SellerOrder).ThenInclude(order => order.Order)
            .ToArrayAsync(cancellationToken);

        var orderItemIds = pageItems.Select(item => item.OrderItemId).Distinct().ToArray();
        var priorRefunds = orderItemIds.Length == 0
            ? new Dictionary<Guid, decimal>()
            : await dbContext.RefundRecords.AsNoTracking()
                .Where(refund => orderItemIds.Contains(refund.ReturnRequest.OrderItemId)
                    && CapCountingStatuses.Contains(refund.Status))
                .GroupBy(refund => refund.ReturnRequest.OrderItemId)
                .Select(group => new { OrderItemId = group.Key, Amount = group.Sum(refund => refund.Amount) })
                .ToDictionaryAsync(item => item.OrderItemId, item => item.Amount, cancellationToken);

        var items = pageItems.Select(item => ToQueueResponse(
            item,
            priorRefunds.GetValueOrDefault(item.OrderItemId))).ToArray();
        return Results.Ok(new PagedResponse<RefundQueueItemResponse>(
            items,
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    private static async Task<IResult> DecideAsync(
        Guid returnRequestId,
        RefundDecisionRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        var isApprove = string.Equals(request.Action, "Approve", StringComparison.Ordinal);
        var isReject = string.Equals(request.Action, "Reject", StringComparison.Ordinal);
        if (!isApprove && !isReject)
            errors["Action"] = ["Action must be Approve or Reject."];
        if (isReject && string.IsNullOrWhiteSpace(request.Reason))
            errors["Reason"] = ["A reason is required when rejecting a return."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;

            try
            {
                if (dbContext.Database.IsRelational())
                {
                    transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);
                }

                var returnRequest = await dbContext.ReturnRequests
                    .Include(item => item.Customer)
                    .Include(item => item.Refunds)
                    .Include(item => item.OrderItem).ThenInclude(item => item.SellerOrder)
                        .ThenInclude(order => order.Order).ThenInclude(order => order.Payments)
                    .SingleOrDefaultAsync(item => item.Id == returnRequestId, cancellationToken);
                if (returnRequest is null) return Results.NotFound();
                if (returnRequest.Status != ReturnStatus.Requested || returnRequest.Refunds.Count > 0)
                    return Results.Problem("This return request has already been decided.",
                        statusCode: StatusCodes.Status409Conflict);

                var now = DateTimeOffset.UtcNow;
                var previousStatus = returnRequest.Status;
                decimal? maximumRefundAmount = null;
                RefundRecord? refundRecord = null;

                if (isApprove)
                {
                    var orderItem = returnRequest.OrderItem;
                    if (orderItem.Quantity <= 0 || returnRequest.Quantity <= 0 || returnRequest.Quantity > orderItem.Quantity)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["Quantity"] = ["Return quantity must be between 1 and the purchased quantity."]
                        });
                    }

                    var paidPayment = orderItem.SellerOrder.Order.Payments
                        .Where(payment => payment.Status is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded)
                        .OrderByDescending(payment => payment.PaidAt)
                        .FirstOrDefault();
                    if (paidPayment is null)
                    {
                        return Results.Problem("Only paid orders can be refunded.",
                            statusCode: StatusCodes.Status409Conflict);
                    }

                    var priorApprovedAmount = await dbContext.RefundRecords.AsNoTracking()
                        .Where(refund => refund.ReturnRequest.OrderItemId == orderItem.Id
                            && refund.ReturnRequestId != returnRequest.Id
                            && CapCountingStatuses.Contains(refund.Status))
                        .SumAsync(refund => (decimal?)refund.Amount, cancellationToken) ?? 0m;
                    maximumRefundAmount = CalculateMaximumRefund(returnRequest, priorApprovedAmount);
                    if (returnRequest.RefundAmount <= 0m || returnRequest.RefundAmount > maximumRefundAmount)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["RefundAmount"] = [$"Requested refund exceeds the available item refund cap of {maximumRefundAmount.Value:0.00}."]
                        });
                    }

                    returnRequest.Status = ReturnStatus.Approved;
                    refundRecord = new RefundRecord
                    {
                        ReturnRequest = returnRequest,
                        PaymentRecord = paidPayment,
                        Status = RefundStatus.Pending,
                        Amount = returnRequest.RefundAmount
                    };
                    dbContext.RefundRecords.Add(refundRecord);
                }
                else
                {
                    returnRequest.Status = ReturnStatus.Rejected;
                }

                returnRequest.ResolvedAt = now;
                dbContext.AuditEntries.Add(new AuditEntry
                {
                    UserId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
                    EntityType = nameof(ReturnRequest),
                    EntityId = returnRequest.Id.ToString(),
                    Action = isApprove ? "RefundApproved" : "RefundRejected",
                    ChangesJson = JsonSerializer.Serialize(new
                    {
                        PreviousStatus = previousStatus.ToString(),
                        NewStatus = returnRequest.Status.ToString(),
                        Reason = NullIfWhiteSpace(request.Reason),
                        RefundRecordId = refundRecord?.Id,
                        refundRecord?.Amount,
                        MaximumRefundAmount = maximumRefundAmount
                    }),
                    CorrelationId = httpContext.TraceIdentifier,
                    OccurredAt = now
                });

                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);

                return Results.Ok(new RefundDecisionResponse(
                    returnRequest.Id,
                    returnRequest.Status.ToString(),
                    now,
                    maximumRefundAmount,
                    refundRecord is null ? null : ToRefundRecordSummary(refundRecord)));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                return Results.Problem("This return request was decided concurrently.",
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (Exception exception) when (ContainsSerializationFailure(exception))
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                return Results.Problem("A concurrent refund decision changed the available item cap. Retry the request.",
                    statusCode: StatusCodes.Status409Conflict);
            }
            finally
            {
                if (transaction is not null) await transaction.DisposeAsync();
            }
        });
    }

    private static Dictionary<string, string[]> ValidateQuery(RefundQueueQuery query)
    {
        var errors = EndpointValidation.Validate(query);
        if (query.From is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero)
            errors["From"] = ["From must be a UTC timestamp."];
        if (query.To is { Offset: var toOffset } && toOffset != TimeSpan.Zero)
            errors["To"] = ["To must be a UTC timestamp."];
        if (query.From is { } from && query.To is { } to && from > to)
            errors["DateRange"] = ["From must be earlier than or equal to To."];
        if (!string.IsNullOrWhiteSpace(query.Status)
            && !Enum.GetNames<ReturnStatus>().Contains(query.Status, StringComparer.Ordinal))
            errors["Status"] = ["Status must be a valid PascalCase return status."];
        return errors;
    }

    private static RefundQueueItemResponse ToQueueResponse(ReturnRequest item, decimal priorRefunds)
    {
        decimal? maximumRefund = item.Status == ReturnStatus.Requested
            ? CalculateMaximumRefund(item, Math.Max(0m, priorRefunds - item.Refunds
                .Where(refund => CapCountingStatuses.Contains(refund.Status))
                .Sum(refund => refund.Amount)))
            : null;
        var orderItem = item.OrderItem;
        return new RefundQueueItemResponse(
            item.Id,
            orderItem.SellerOrder.OrderId,
            orderItem.SellerOrder.Order.OrderNumber,
            orderItem.SellerOrderId,
            orderItem.SellerOrder.StoreId,
            orderItem.SellerOrder.Store.Name,
            orderItem.Id,
            orderItem.ProductId,
            orderItem.SkuSnapshot,
            orderItem.ProductNameSnapshot,
            orderItem.Quantity,
            item.Quantity,
            orderItem.UnitPrice,
            orderItem.DiscountAmount,
            orderItem.LineTotal,
            item.RefundAmount,
            maximumRefund,
            orderItem.SellerOrder.Order.Currency,
            item.Customer.DisplayName,
            item.Customer.Email ?? string.Empty,
            item.Reason,
            item.Details,
            item.Status.ToString(),
            item.RequestedAt,
            item.ResolvedAt,
            item.Refunds.Select(ToRefundRecordSummary).ToArray());
    }

    private static decimal CalculateMaximumRefund(ReturnRequest item, decimal priorApprovedAmount)
    {
        var orderItem = item.OrderItem;
        if (orderItem.Quantity <= 0 || item.Quantity <= 0) return 0m;
        var lineTotal = Math.Max(0m, orderItem.LineTotal);
        var allocatedAmount = decimal.Round(
            lineTotal * item.Quantity / orderItem.Quantity,
            2,
            MidpointRounding.AwayFromZero);
        var remainingLineTotal = Math.Max(0m, lineTotal - priorApprovedAmount);
        return Math.Min(allocatedAmount, remainingLineTotal);
    }

    private static RefundRecordSummary ToRefundRecordSummary(RefundRecord refund) => new(
        refund.Id,
        refund.Amount,
        refund.Status.ToString(),
        refund.CreatedAt);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ContainsSerializationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException
                && postgresException.SqlState == PostgresErrorCodes.SerializationFailure)
            {
                return true;
            }
        }
        return false;
    }
}
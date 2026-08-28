using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class FulfilmentEndpoints
{
    private static readonly SellerOrderStatus[] QueueStatuses =
    [
        SellerOrderStatus.ReadyForFulfilment,
        SellerOrderStatus.Picking,
        SellerOrderStatus.Packed,
        SellerOrderStatus.Shipped
    ];

    public static IEndpointRouteBuilder MapFulfilmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/fulfilment")
            .WithTags("Fulfilment")
            .RequireAuthorization(AuthorizationPolicies.Fulfilment);
        group.MapGet("/orders", GetQueueAsync).Produces<IReadOnlyCollection<FulfilmentOrderResponse>>();
        group.MapPost("/orders/{sellerOrderId:guid}/transition", TransitionAsync)
            .Produces<FulfilmentOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints;
    }

    private static async Task<IResult> GetQueueAsync(
        [AsParameters] FulfilmentQuery query,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        SellerOrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<SellerOrderStatus>(query.Status, true, out var parsed))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Status"] = ["The fulfilment status is invalid."]
                });
            }
            status = parsed;
        }

        var orders = BaseQuery(dbContext).AsNoTracking();
        if (IsSeller(principal, out var sellerId)) orders = orders.Where(order => order.SellerId == sellerId);
        orders = status is null
            ? orders.Where(order => QueueStatuses.Contains(order.Status))
            : orders.Where(order => order.Status == status);
        var result = await orders.OrderBy(order => order.Order.PaidAt).Take(200).ToArrayAsync(cancellationToken);
        return Results.Ok(result.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> TransitionAsync(
        Guid sellerOrderId,
        FulfilmentTransitionRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        var action = request.Action?.Trim().ToLowerInvariant();
        if (action is not ("startpicking" or "pack" or "dispatch" or "deliver"))
            errors["Action"] = ["Use StartPicking, Pack, Dispatch, or Deliver."];
        if (action == "dispatch")
        {
            if (string.IsNullOrWhiteSpace(request.Carrier)) errors["Carrier"] = ["Carrier is required for dispatch."];
            if (string.IsNullOrWhiteSpace(request.TrackingNumber))
                errors["TrackingNumber"] = ["Tracking number is required for dispatch."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var order = await BaseQuery(dbContext).SingleOrDefaultAsync(candidate => candidate.Id == sellerOrderId,
            cancellationToken);
        if (order is null || IsSeller(principal, out var sellerId) && order.SellerId != sellerId)
            return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        var expectedStatus = action switch
        {
            "startpicking" => SellerOrderStatus.ReadyForFulfilment,
            "pack" => SellerOrderStatus.Picking,
            "dispatch" => SellerOrderStatus.Packed,
            "deliver" => SellerOrderStatus.Shipped,
            _ => throw new InvalidOperationException("Validated action was not recognized.")
        };
        if (order.Status != expectedStatus)
        {
            return Results.Problem(
                $"{request.Action} is not allowed while the seller order is {order.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        switch (action)
        {
            case "startpicking":
                order.Status = SellerOrderStatus.Picking;
                break;
            case "pack":
                order.Status = SellerOrderStatus.Packed;
                var packedShipment = new Shipment { SellerOrder = order, Status = ShipmentStatus.Packed };
                order.Shipments.Add(packedShipment);
                dbContext.Shipments.Add(packedShipment);
                break;
            case "dispatch":
                order.Status = SellerOrderStatus.Shipped;
                var shipment = order.Shipments.SingleOrDefault(item => item.Status == ShipmentStatus.Packed);
                if (shipment is null)
                {
                    return Results.Problem("The packed shipment record is missing.",
                        statusCode: StatusCodes.Status409Conflict);
                }
                shipment.Status = ShipmentStatus.Dispatched;
                shipment.Carrier = request.Carrier!.Trim();
                shipment.TrackingNumber = request.TrackingNumber!.Trim();
                shipment.DispatchedAt = now;
                await UpdateParentOrderStatusAsync(dbContext, order.OrderId, dispatched: true, cancellationToken);
                break;
            case "deliver":
                order.Status = SellerOrderStatus.Delivered;
                var dispatched = order.Shipments.SingleOrDefault(item => item.Status is
                    ShipmentStatus.Dispatched or ShipmentStatus.InTransit);
                if (dispatched is null)
                {
                    return Results.Problem("The dispatched shipment record is missing.",
                        statusCode: StatusCodes.Status409Conflict);
                }
                dispatched.Status = ShipmentStatus.Delivered;
                dispatched.DeliveredAt = now;
                await UpdateParentOrderStatusAsync(dbContext, order.OrderId, dispatched: false, cancellationToken);
                break;
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            UserId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            EntityType = nameof(SellerOrder),
            EntityId = order.Id.ToString(),
            Action = $"Fulfilment{request.Action!.Trim()}",
            CorrelationId = httpContext.TraceIdentifier,
            OccurredAt = now,
            ChangesJson = JsonSerializer.Serialize(new { PreviousStatus = expectedStatus, NewStatus = order.Status })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(order));
    }

    private static async Task UpdateParentOrderStatusAsync(
        MarketplaceDbContext dbContext,
        Guid orderId,
        bool dispatched,
        CancellationToken cancellationToken)
    {
        var parent = await dbContext.Orders.Include(order => order.SellerOrders)
            .SingleAsync(order => order.Id == orderId, cancellationToken);
        if (dispatched)
        {
            parent.Status = parent.SellerOrders.All(order =>
                    order.Status is SellerOrderStatus.Shipped or SellerOrderStatus.Delivered)
                ? OrderStatus.Shipped
                : OrderStatus.PartiallyShipped;
        }
        else if (parent.SellerOrders.All(order => order.Status == SellerOrderStatus.Delivered))
        {
            parent.Status = OrderStatus.Delivered;
        }
    }

    private static IQueryable<SellerOrder> BaseQuery(MarketplaceDbContext dbContext) =>
        dbContext.SellerOrders
            .Include(order => order.Store)
            .Include(order => order.Order).ThenInclude(parent => parent.ShippingAddress)
            .Include(order => order.Items)
            .Include(order => order.Shipments);

    private static bool IsSeller(ClaimsPrincipal principal, out Guid sellerId)
    {
        sellerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return principal.IsInRole(AppRoles.Seller)
            && !principal.IsInRole(AppRoles.FulfilmentEmployee)
            && !principal.IsInRole(AppRoles.SystemAdministrator);
    }

    private static FulfilmentOrderResponse ToResponse(SellerOrder order)
    {
        var shipment = order.Shipments.OrderByDescending(item => item.CreatedAt).FirstOrDefault();
        return new FulfilmentOrderResponse(
            order.Id,
            order.OrderId,
            order.Order.OrderNumber,
            order.StoreId,
            order.Store.Name,
            order.Status.ToString(),
            order.Order.PaidAt ?? order.Order.UpdatedAt,
            order.Order.ShippingAddress.RecipientName,
            order.Order.ShippingAddress.City,
            order.Order.ShippingAddress.Province,
            order.Items.Select(item => new FulfilmentItemResponse(
                item.Id, item.ProductId, item.SkuSnapshot, item.ProductNameSnapshot, item.Quantity)).ToArray(),
            shipment is null ? null : new FulfilmentShipmentResponse(
                shipment.Id,
                shipment.Status.ToString(),
                shipment.Carrier,
                shipment.TrackingNumber,
                shipment.DispatchedAt,
                shipment.DeliveredAt));
    }
}

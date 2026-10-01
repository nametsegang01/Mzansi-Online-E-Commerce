using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/orders")
            .WithTags("Customer orders")
            .RequireAuthorization(AuthorizationPolicies.CustomerAccess)
            .MapGet("/", GetOrdersAsync)
            .Produces<IReadOnlyCollection<CustomerOrderResponse>>();

        return endpoints;
    }

    private static async Task<IResult> GetOrdersAsync(
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var customerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var orders = await dbContext.Orders.AsNoTracking()
            .Include(order => order.SellerOrders).ThenInclude(sellerOrder => sellerOrder.Items)
            .Include(order => order.SellerOrders).ThenInclude(sellerOrder => sellerOrder.Shipments)
            .Where(order => order.CustomerId == customerId)
            .OrderByDescending(order => order.PlacedAt ?? order.CreatedAt)
            .ToArrayAsync(cancellationToken);

        var response = orders.Select(order => new CustomerOrderResponse(
            order.OrderNumber,
            order.PlacedAt ?? order.CreatedAt,
            order.Status.ToString(),
            order.GrandTotal,
            order.Currency,
            order.SellerOrders.SelectMany(sellerOrder => sellerOrder.Items)
                .OrderBy(item => item.ProductNameSnapshot)
                .Select(item => new CustomerOrderItemResponse(item.ProductNameSnapshot, item.Quantity))
                .ToArray(),
            order.SellerOrders.Select(sellerOrder =>
            {
                var shipment = sellerOrder.Shipments
                    .OrderByDescending(candidate => candidate.CreatedAt)
                    .FirstOrDefault();
                return new CustomerSellerOrderResponse(
                    sellerOrder.Status.ToString(),
                    shipment is null
                        ? null
                        : new CustomerShipmentResponse(
                            shipment.Status.ToString(),
                            shipment.Carrier,
                            shipment.TrackingNumber,
                            shipment.DispatchedAt,
                            shipment.DeliveredAt));
            }).ToArray()))
            .ToArray();

        return Results.Ok(response);
    }
}
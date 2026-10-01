using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class CustomerOrdersApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Orders_ReturnOnlyCustomersOwnOrdersAndIncludeEveryStatusAndShipment()
    {
        var customer = await RegisterCustomerAsync("order-history-owner");
        var otherCustomer = await RegisterCustomerAsync("order-history-other");
        var now = DateTimeOffset.UtcNow;

        var multiSellerOrderNumber = "ORDER-MULTI-SELLER";
        await SeedOrderAsync(customer.UserId, multiSellerOrderNumber, OrderStatus.Shipped, now,
            CreateSellerOrder(
                "Handwoven Basket", 2, SellerOrderStatus.Shipped,
                new Shipment
                {
                    Status = ShipmentStatus.Dispatched,
                    Carrier = "Courier One",
                    TrackingNumber = "TRACK-ONE",
                    DispatchedAt = now.AddHours(-2)
                }),
            CreateSellerOrder(
                "Ceramic Mug", 1, SellerOrderStatus.Delivered,
                new Shipment
                {
                    Status = ShipmentStatus.Delivered,
                    Carrier = "Courier Two",
                    TrackingNumber = "TRACK-TWO",
                    DispatchedAt = now.AddDays(-1),
                    DeliveredAt = now.AddHours(-1)
                }));
        await SeedOrderAsync(customer.UserId, "ORDER-PENDING", OrderStatus.PendingPayment, now.AddDays(-1),
            CreateSellerOrder("Pending Item", 1, SellerOrderStatus.Pending, null));
        await SeedOrderAsync(customer.UserId, "ORDER-CANCELLED", OrderStatus.Cancelled, now.AddDays(-2),
            CreateSellerOrder("Cancelled Item", 3, SellerOrderStatus.Cancelled, null));
        await SeedOrderAsync(otherCustomer.UserId, "OTHER-CUSTOMER-ORDER", OrderStatus.Paid, now.AddDays(1));

        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var orders = body.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, orders.Length);
        Assert.Equal(multiSellerOrderNumber, orders[0].GetProperty("orderNumber").GetString());
        Assert.Equal("Shipped", orders[0].GetProperty("status").GetString());
        Assert.Equal("ZAR", orders[0].GetProperty("currency").GetString());
        Assert.Equal(2, orders[0].GetProperty("items").GetArrayLength());
        Assert.Contains(orders[0].GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "Handwoven Basket"
            && item.GetProperty("quantity").GetInt32() == 2);

        var sellerOrders = orders[0].GetProperty("sellerOrders").EnumerateArray().ToArray();
        Assert.Equal(2, sellerOrders.Length);
        var dispatchedShipment = Assert.Single(sellerOrders, sellerOrder =>
            sellerOrder.GetProperty("shipment").GetProperty("trackingNumber").GetString() == "TRACK-ONE");
        Assert.Equal("Dispatched", dispatchedShipment.GetProperty("shipment").GetProperty("status").GetString());
        Assert.Equal("Courier One", dispatchedShipment.GetProperty("shipment").GetProperty("carrier").GetString());
        Assert.Equal(now.AddHours(-2), dispatchedShipment.GetProperty("shipment").GetProperty("dispatchedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, dispatchedShipment.GetProperty("shipment").GetProperty("deliveredAt").ValueKind);

        var deliveredShipment = Assert.Single(sellerOrders, sellerOrder =>
            sellerOrder.GetProperty("shipment").GetProperty("trackingNumber").GetString() == "TRACK-TWO");
        Assert.Equal("Delivered", deliveredShipment.GetProperty("shipment").GetProperty("status").GetString());
        Assert.Equal(now.AddHours(-1), deliveredShipment.GetProperty("shipment").GetProperty("deliveredAt").GetDateTimeOffset());

        Assert.Equal("PendingPayment", orders[1].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, orders[1].GetProperty("sellerOrders")[0].GetProperty("shipment").ValueKind);
        Assert.Equal("Cancelled", orders[2].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, orders[2].GetProperty("sellerOrders")[0].GetProperty("shipment").ValueKind);
        Assert.DoesNotContain(orders, order => order.GetProperty("orderNumber").GetString() == "OTHER-CUSTOMER-ORDER");
        Assert.All(orders, order =>
        {
            Assert.False(order.TryGetProperty("customerId", out _));
            foreach (var sellerOrder in order.GetProperty("sellerOrders").EnumerateArray())
            {
                Assert.False(sellerOrder.TryGetProperty("sellerId", out _));
                Assert.False(sellerOrder.TryGetProperty("storeId", out _));
            }
        });
    }

    [Fact]
    public async Task Orders_RequireAnAuthenticatedCustomer()
    {
        using var anonymousClient = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/orders")).StatusCode);

        var email = $"seller-only-{Guid.NewGuid():N}@example.test";
        const string password = "SellerOnly!2345";
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                DisplayName = "Seller Only",
                Status = AccountStatus.Active
            };
            Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, AppRoles.Seller)).Succeeded);
        }

        using var sellerClient = factory.CreateApiClient();
        var login = await sellerClient.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        sellerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", loginBody.RootElement.GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.Forbidden,
            (await sellerClient.GetAsync("/api/orders")).StatusCode);
    }

    private async Task<(Guid UserId, string AccessToken)> RegisterCustomerAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.test";
        const string password = "CustomerOnly!2345";
        using var client = factory.CreateApiClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Test",
            lastName = "Customer"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registrationBody = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        var userId = registrationBody.RootElement.GetProperty("userId").GetGuid();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return (userId, loginBody.RootElement.GetProperty("accessToken").GetString()!);
    }

    private async Task SeedOrderAsync(
        Guid customerId,
        string orderNumber,
        OrderStatus status,
        DateTimeOffset placedAt,
        params SellerOrder[] sellerOrders)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var order = new Order
        {
            OrderNumber = orderNumber,
            CheckoutKey = $"history-{Guid.NewGuid():N}",
            CustomerId = customerId,
            Status = status,
            Subtotal = 500m,
            GrandTotal = 500m,
            Currency = "ZAR",
            PlacedAt = placedAt
        };
        foreach (var sellerOrder in sellerOrders)
        {
            sellerOrder.Order = order;
            order.SellerOrders.Add(sellerOrder);
        }
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();
    }

    private static SellerOrder CreateSellerOrder(
        string productName,
        int quantity,
        SellerOrderStatus status,
        Shipment? shipment)
    {
        var sellerOrder = new SellerOrder
        {
            SellerId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            Status = status,
            Subtotal = 100m,
            SellerNetAmount = 100m
        };
        sellerOrder.Items.Add(new OrderItem
        {
            ProductId = Guid.NewGuid(),
            SkuSnapshot = $"SKU-{productName.Replace(' ', '-')}",
            ProductNameSnapshot = productName,
            Quantity = quantity,
            UnitPrice = 100m,
            LineTotal = 100m
        });
        if (shipment is not null)
        {
            sellerOrder.Shipments.Add(shipment);
        }
        return sellerOrder;
    }
}
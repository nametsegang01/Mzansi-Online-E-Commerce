using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class FulfilmentApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ApprovedSeller_SeesOwnQueueAndCompletesControlledTransitions()
    {
        var first = await RegisterApprovedSellerAsync("fulfilment-first");
        var second = await RegisterApprovedSellerAsync("fulfilment-second");
        var firstOrderId = await SeedReadyOrderAsync(first.UserId, first.StoreId, "FIRST");
        await SeedReadyOrderAsync(second.UserId, second.StoreId, "SECOND");

        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
        var queue = await client.GetAsync("/api/fulfilment/orders");
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        using var queueBody = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
        Assert.Single(queueBody.RootElement.EnumerateArray());
        Assert.Equal(firstOrderId, queueBody.RootElement[0].GetProperty("sellerOrderId").GetGuid());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/fulfilment/orders?status=Invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await TransitionAsync(client, firstOrderId, "Pack")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await TransitionAsync(client, firstOrderId, "StartPicking")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await TransitionAsync(client, firstOrderId, "Pack")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await TransitionAsync(client, firstOrderId, "Dispatch")).StatusCode);
        var dispatched = await TransitionAsync(
            client, firstOrderId, "Dispatch", "Mzansi Courier", "TRACK-0001");
        Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode);
        using var dispatchedBody = JsonDocument.Parse(await dispatched.Content.ReadAsStringAsync());
        Assert.Equal("Shipped", dispatchedBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("TRACK-0001",
            dispatchedBody.RootElement.GetProperty("shipment").GetProperty("trackingNumber").GetString());
        Assert.Equal(HttpStatusCode.OK,
            (await TransitionAsync(client, firstOrderId, "Deliver")).StatusCode);

        using var secondClient = factory.CreateApiClient();
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.Token);
        Assert.Equal(HttpStatusCode.NotFound,
            (await TransitionAsync(secondClient, firstOrderId, "StartPicking")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var sellerOrder = await dbContext.SellerOrders.Include(order => order.Order).Include(order => order.Shipments)
            .SingleAsync(order => order.Id == firstOrderId);
        Assert.Equal(SellerOrderStatus.Delivered, sellerOrder.Status);
        Assert.Equal(OrderStatus.Delivered, sellerOrder.Order.Status);
        var shipment = Assert.Single(sellerOrder.Shipments);
        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
        Assert.NotNull(shipment.DeliveredAt);
        Assert.Equal(4, await dbContext.AuditEntries.CountAsync(entry =>
            entry.EntityId == firstOrderId.ToString() && entry.Action.StartsWith("Fulfilment")));
    }

    private static async Task<HttpResponseMessage> TransitionAsync(
        HttpClient client,
        Guid sellerOrderId,
        string action,
        string? carrier = null,
        string? trackingNumber = null) =>
        await client.PostAsJsonAsync($"/api/fulfilment/orders/{sellerOrderId}/transition",
            new { action, carrier, trackingNumber });

    private async Task<SellerSetup> RegisterApprovedSellerAsync(string prefix)
    {
        using var client = factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"{prefix}-{suffix}@example.test";
        const string password = "LocalOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/seller", new
        {
            email,
            password,
            firstName = "Lerato",
            lastName = "Mokoena",
            tradingName = $"Seller {suffix}",
            registrationNumber = $"TEST-{suffix[..8]}",
            storeSlug = $"seller-{suffix}",
            supportEmail = email
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        Guid userId;
        Guid storeId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            userId = user.Id;
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var seller = await dbContext.SellerProfiles.SingleAsync(item => item.UserId == userId);
            var store = await dbContext.Stores.SingleAsync(item => item.SellerId == userId);
            seller.Status = SellerStatus.Approved;
            store.Status = StoreStatus.Active;
            storeId = store.Id;
            await dbContext.SaveChangesAsync();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return new SellerSetup(userId, storeId, body.RootElement.GetProperty("accessToken").GetString()!);
    }

    private async Task<Guid> SeedReadyOrderAsync(Guid sellerId, Guid storeId, string discriminator)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var store = await dbContext.Stores.Include(item => item.Seller).SingleAsync(item => item.Id == storeId);
        var category = new Category
        {
            Name = $"Fulfilment {suffix}",
            Slug = $"fulfilment-{suffix}",
            IsActive = true
        };
        var product = new Product
        {
            Store = store,
            Sku = $"FULFIL-{discriminator}-{suffix}",
            Name = $"Fulfilment item {discriminator}",
            Slug = $"fulfilment-{discriminator.ToLowerInvariant()}-{suffix}",
            Price = 250m,
            Status = ProductStatus.Active,
            Inventory = new InventoryItem { OnHandQuantity = 8, ReorderLevel = 1 }
        };
        product.Inventory.Product = product;
        product.Categories.Add(new ProductCategory { Product = product, Category = category });
        var order = new Order
        {
            OrderNumber = $"TEST-{suffix}",
            CheckoutKey = $"fulfilment-{suffix}",
            CustomerId = sellerId,
            Status = OrderStatus.Paid,
            Subtotal = 250m,
            DeliveryTotal = 75m,
            GrandTotal = 325m,
            PlacedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            PaidAt = DateTimeOffset.UtcNow.AddMinutes(-9),
            ShippingAddress = new OrderAddress
            {
                RecipientName = "Test Customer",
                Line1 = "1 Test Street",
                City = "Johannesburg",
                Province = "Gauteng",
                PostalCode = "2000",
                CountryCode = "ZA"
            }
        };
        var sellerOrder = new SellerOrder
        {
            Order = order,
            SellerId = sellerId,
            Store = store,
            Status = SellerOrderStatus.ReadyForFulfilment,
            Subtotal = 250m,
            DeliveryTotal = 75m,
            CommissionAmount = 25m,
            SellerNetAmount = 225m
        };
        sellerOrder.Items.Add(new OrderItem
        {
            Product = product,
            SkuSnapshot = product.Sku,
            ProductNameSnapshot = product.Name,
            Quantity = 1,
            UnitPrice = 250m,
            LineTotal = 250m,
            Reservation = new StockReservation
            {
                ProductId = product.Id,
                Quantity = 1,
                Status = StockReservationStatus.Committed,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            }
        });
        order.SellerOrders.Add(sellerOrder);
        dbContext.AddRange(category, product, order);
        await dbContext.SaveChangesAsync();
        return sellerOrder.Id;
    }

    private sealed record SellerSetup(Guid UserId, Guid StoreId, string Token);
}

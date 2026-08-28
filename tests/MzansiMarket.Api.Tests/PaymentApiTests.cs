using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class PaymentApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task PaidSandboxEvent_CommitsReservationAndDuplicateIsSafe()
    {
        var setup = await CreatePendingOrderAsync("payment-paid");
        var started = await StartPaymentAsync(setup.Client, setup.OrderId, "payment-start-key-0001");
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var startedBody = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var paymentId = startedBody.RootElement.GetProperty("paymentId").GetGuid();
        var reference = startedBody.RootElement.GetProperty("providerReference").GetString()!;
        Assert.Equal("Pending", startedBody.RootElement.GetProperty("status").GetString());

        var replayed = await StartPaymentAsync(setup.Client, setup.OrderId, "payment-start-key-0001");
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        using var replayBody = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync());
        Assert.Equal(paymentId, replayBody.RootElement.GetProperty("paymentId").GetGuid());

        var missingSecret = await SendEventAsync(setup.Client, "payment-event-paid-0001", reference, "Paid", null);
        Assert.Equal(HttpStatusCode.Unauthorized, missingSecret.StatusCode);
        var paid = await SendEventAsync(
            setup.Client, "payment-event-paid-0001", reference, "Paid", "test-webhook-secret-only");
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        using var paidBody = JsonDocument.Parse(await paid.Content.ReadAsStringAsync());
        Assert.False(paidBody.RootElement.GetProperty("duplicate").GetBoolean());
        Assert.Equal("Paid", paidBody.RootElement.GetProperty("paymentStatus").GetString());
        Assert.Equal("Paid", paidBody.RootElement.GetProperty("orderStatus").GetString());

        var duplicate = await SendEventAsync(
            setup.Client, "payment-event-paid-0001", reference, "Paid", "test-webhook-secret-only");
        using var duplicateBody = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.True(duplicateBody.RootElement.GetProperty("duplicate").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var inventory = await dbContext.InventoryItems.SingleAsync(item => item.ProductId == setup.ProductId);
        Assert.Equal(8, inventory.OnHandQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.Equal(1, await dbContext.PaymentProviderEvents.CountAsync(item => item.EventId == "payment-event-paid-0001"));
        Assert.Equal(1, await dbContext.InventoryTransactions.CountAsync(item =>
            item.ProductId == setup.ProductId && item.Type == InventoryTransactionType.Sale));
        Assert.Equal(StockReservationStatus.Committed,
            (await dbContext.StockReservations.SingleAsync(item => item.ProductId == setup.ProductId)).Status);
        setup.Client.Dispose();
    }

    [Fact]
    public async Task FailedSandboxEvent_ReleasesReservationAndCancelsOrder()
    {
        var setup = await CreatePendingOrderAsync("payment-failed");
        var started = await StartPaymentAsync(setup.Client, setup.OrderId, "payment-start-key-0002");
        using var startedBody = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var reference = startedBody.RootElement.GetProperty("providerReference").GetString()!;

        var invalidOutcome = await SendEventAsync(
            setup.Client, "payment-event-invalid", reference, "Unknown", "test-webhook-secret-only");
        Assert.Equal(HttpStatusCode.BadRequest, invalidOutcome.StatusCode);
        var failed = await SendEventAsync(
            setup.Client, "payment-event-failed-0001", reference, "Failed", "test-webhook-secret-only");
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        using var failedBody = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
        Assert.Equal("Failed", failedBody.RootElement.GetProperty("paymentStatus").GetString());
        Assert.Equal("Cancelled", failedBody.RootElement.GetProperty("orderStatus").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var inventory = await dbContext.InventoryItems.SingleAsync(item => item.ProductId == setup.ProductId);
        Assert.Equal(10, inventory.OnHandQuantity);
        Assert.Equal(0, inventory.ReservedQuantity);
        Assert.Equal(StockReservationStatus.Released,
            (await dbContext.StockReservations.SingleAsync(item => item.ProductId == setup.ProductId)).Status);
        Assert.Equal(SellerOrderStatus.Cancelled,
            (await dbContext.SellerOrders.SingleAsync(item => item.OrderId == setup.OrderId)).Status);
        setup.Client.Dispose();
    }

    private async Task<PendingOrderSetup> CreatePendingOrderAsync(string prefix)
    {
        var productId = await SeedProductAsync();
        var client = factory.CreateApiClient();
        var token = await RegisterAndLoginAsync(client, prefix);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var addressResponse = await client.PostAsJsonAsync("/api/account/addresses", new
        {
            type = "Shipping",
            recipientName = "Naledi Dlamini",
            line1 = "1 Payment Street",
            city = "Johannesburg",
            province = "Gauteng",
            postalCode = "2000",
            countryCode = "ZA",
            isDefault = true
        });
        using var addressBody = JsonDocument.Parse(await addressResponse.Content.ReadAsStringAsync());
        var addressId = addressBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/cart/items", new { productId, quantity = 2 })).StatusCode);
        using var checkout = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { addressId })
        };
        checkout.Headers.Add("Idempotency-Key", $"{prefix}-checkout-key");
        var checkoutResponse = await client.SendAsync(checkout);
        Assert.Equal(HttpStatusCode.Created, checkoutResponse.StatusCode);
        using var checkoutBody = JsonDocument.Parse(await checkoutResponse.Content.ReadAsStringAsync());
        return new PendingOrderSetup(client, checkoutBody.RootElement.GetProperty("orderId").GetGuid(), productId);
    }

    private static async Task<HttpResponseMessage> StartPaymentAsync(HttpClient client, Guid orderId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments/sandbox")
        {
            Content = JsonContent.Create(new { paymentMethodType = "TestWallet" })
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendEventAsync(
        HttpClient client,
        string eventId,
        string reference,
        string outcome,
        string? secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/sandbox/events")
        {
            Content = JsonContent.Create(new { eventId, providerReference = reference, outcome })
        };
        if (secret is not null) request.Headers.Add("X-Sandbox-Webhook-Secret", secret);
        return await client.SendAsync(request);
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Naledi",
            lastName = "Dlamini"
        })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> SeedProductAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var sellerUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"payment-seller-{suffix}@example.test",
            NormalizedUserName = $"PAYMENT-SELLER-{suffix}@EXAMPLE.TEST",
            Email = $"payment-seller-{suffix}@example.test",
            NormalizedEmail = $"PAYMENT-SELLER-{suffix}@EXAMPLE.TEST",
            DisplayName = "Payment Seller",
            Status = AccountStatus.Active
        };
        var seller = new SellerProfile
        {
            User = sellerUser,
            TradingName = "Payment Store",
            Status = SellerStatus.Approved,
            CommissionRate = 0.1m
        };
        sellerUser.SellerProfile = seller;
        var store = new Store
        {
            Seller = seller,
            Name = $"Payment Store {suffix}",
            Slug = $"payment-store-{suffix}",
            Status = StoreStatus.Active
        };
        var category = new Category
        {
            Name = $"Payment Category {suffix}",
            Slug = $"payment-category-{suffix}",
            IsActive = true
        };
        var product = new Product
        {
            Store = store,
            Sku = $"PAYMENT-{suffix}",
            Name = "Sandbox Product",
            Slug = $"sandbox-product-{suffix}",
            Price = 100m,
            Currency = "ZAR",
            Status = ProductStatus.Active,
            Inventory = new InventoryItem { OnHandQuantity = 10, ReorderLevel = 1 }
        };
        product.Inventory.Product = product;
        product.Categories.Add(new ProductCategory { Product = product, Category = category });
        dbContext.AddRange(store, category, product);
        await dbContext.SaveChangesAsync();
        return product.Id;
    }

    private sealed record PendingOrderSetup(HttpClient Client, Guid OrderId, Guid ProductId);
}

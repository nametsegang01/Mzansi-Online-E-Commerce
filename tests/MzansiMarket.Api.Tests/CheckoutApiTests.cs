using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class CheckoutApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Checkout_CreatesMultiSellerOrderAndIsIdempotent()
    {
        var seed = await SeedCheckoutCatalogueAsync();
        using var client = factory.CreateApiClient();
        var token = await RegisterAndLoginAsync(client, "checkout");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var addressId = await CreateAddressAsync(client, "Shipping");
        await AddAsync(client, seed.FirstProductId, 2);
        await AddAsync(client, seed.SecondProductId, 1);

        var missingKey = await client.PostAsJsonAsync("/api/checkout", new
        {
            addressId,
            promotionCode = seed.PromotionCode
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);

        const string checkoutKey = "checkout-idempotency-test-0001";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { addressId, promotionCode = seed.PromotionCode })
        };
        request.Headers.Add("Idempotency-Key", checkoutKey);
        var placed = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        using var body = JsonDocument.Parse(await placed.Content.ReadAsStringAsync());
        var orderId = body.RootElement.GetProperty("orderId").GetGuid();
        Assert.Equal(1400m, body.RootElement.GetProperty("subtotal").GetDecimal());
        Assert.Equal(140m, body.RootElement.GetProperty("discountTotal").GetDecimal());
        Assert.Equal(75m, body.RootElement.GetProperty("deliveryTotal").GetDecimal());
        Assert.Equal(1335m, body.RootElement.GetProperty("grandTotal").GetDecimal());
        Assert.Equal(2, body.RootElement.GetProperty("sellerOrders").GetArrayLength());
        Assert.Equal("1 Checkout Street",
            body.RootElement.GetProperty("shippingAddress").GetProperty("line1").GetString());

        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { addressId, promotionCode = seed.PromotionCode })
        };
        replay.Headers.Add("Idempotency-Key", checkoutKey);
        var replayed = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        using var replayBody = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync());
        Assert.Equal(orderId, replayBody.RootElement.GetProperty("orderId").GetGuid());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var order = await dbContext.Orders
            .Include(candidate => candidate.SellerOrders).ThenInclude(candidate => candidate.Items)
                .ThenInclude(item => item.Reservation)
            .SingleAsync(candidate => candidate.Id == orderId);
        Assert.Equal(2, order.SellerOrders.Count);
        Assert.Equal(2, order.SellerOrders.SelectMany(candidate => candidate.Items).Count());
        Assert.All(order.SellerOrders.SelectMany(candidate => candidate.Items),
            item => Assert.Equal(StockReservationStatus.Active, item.Reservation!.Status));
        Assert.Equal(CartStatus.Converted,
            (await dbContext.Carts.SingleAsync(candidate => candidate.CustomerId == order.CustomerId)).Status);
        Assert.Equal(1, await dbContext.Orders.CountAsync(candidate =>
            candidate.CustomerId == order.CustomerId && candidate.CheckoutKey == checkoutKey));
        Assert.Equal(1, await dbContext.AuditEntries.CountAsync(candidate =>
            candidate.EntityId == orderId.ToString() && candidate.Action == "CheckoutPlaced"));
        Assert.Equal(2, (await dbContext.InventoryItems.SingleAsync(candidate =>
            candidate.ProductId == seed.FirstProductId)).ReservedQuantity);
        Assert.Equal(1, (await dbContext.InventoryItems.SingleAsync(candidate =>
            candidate.ProductId == seed.SecondProductId)).ReservedQuantity);
    }

    [Fact]
    public async Task Checkout_RejectsBillingOnlyAddressInvalidPromotionAndChangedStock()
    {
        var seed = await SeedCheckoutCatalogueAsync();
        using var client = factory.CreateApiClient();
        var token = await RegisterAndLoginAsync(client, "checkout-negative");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var billingAddress = await CreateAddressAsync(client, "Billing");
        var shippingAddress = await CreateAddressAsync(client, "Shipping");
        await AddAsync(client, seed.FirstProductId, 1);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await CheckoutAsync(client, billingAddress, "billing-only-key", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await CheckoutAsync(client, shippingAddress, "invalid-promo-key", "DOES-NOT-EXIST")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var inventory = await dbContext.InventoryItems.SingleAsync(candidate =>
                candidate.ProductId == seed.FirstProductId);
            inventory.OnHandQuantity = 0;
            await dbContext.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict,
            (await CheckoutAsync(client, shippingAddress, "stock-changed-key", null)).StatusCode);
    }

    [Fact]
    public async Task Checkout_RequiresCustomerAuthentication()
    {
        using var client = factory.CreateApiClient();
        var response = await CheckoutAsync(client, Guid.NewGuid(), "anonymous-key", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> CheckoutAsync(
        HttpClient client,
        Guid addressId,
        string key,
        string? promotionCode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { addressId, promotionCode })
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateAddressAsync(HttpClient client, string type)
    {
        var response = await client.PostAsJsonAsync("/api/account/addresses", new
        {
            type,
            recipientName = "Naledi Dlamini",
            line1 = "1 Checkout Street",
            city = "Johannesburg",
            province = "Gauteng",
            postalCode = "2000",
            countryCode = "ZA",
            isDefault = false
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AddAsync(HttpClient client, Guid productId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/api/cart/items", new { productId, quantity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<CheckoutSeed> SeedCheckoutCatalogueAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category
        {
            Name = $"Checkout {suffix}",
            Slug = $"checkout-{suffix}",
            IsActive = true
        };
        var firstStore = CreateStore(suffix, "first", 0.10m);
        var secondStore = CreateStore(suffix, "second", 0.15m);
        var firstProduct = CreateProduct(firstStore, category, $"CHECKOUT-1-{suffix}", 600m);
        var secondProduct = CreateProduct(secondStore, category, $"CHECKOUT-2-{suffix}", 200m);
        var promotion = new Promotion
        {
            Code = $"SAVE10{suffix[..8]}".ToUpperInvariant(),
            Name = "Ten percent checkout test",
            Type = PromotionType.Percentage,
            Status = PromotionStatus.Active,
            Value = 10m,
            MinimumOrderAmount = 100m,
            UsageLimit = 10,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
            EndsAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        dbContext.AddRange(category, firstStore, secondStore, firstProduct, secondProduct, promotion);
        await dbContext.SaveChangesAsync();
        return new CheckoutSeed(firstProduct.Id, secondProduct.Id, promotion.Code);
    }

    private static Store CreateStore(string suffix, string discriminator, decimal commissionRate)
    {
        var seller = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"seller-{discriminator}-{suffix}@example.test",
            NormalizedUserName = $"SELLER-{discriminator}-{suffix}@EXAMPLE.TEST",
            Email = $"seller-{discriminator}-{suffix}@example.test",
            NormalizedEmail = $"SELLER-{discriminator}-{suffix}@EXAMPLE.TEST",
            DisplayName = $"Seller {discriminator}",
            Status = AccountStatus.Active
        };
        var profile = new SellerProfile
        {
            User = seller,
            TradingName = $"Store {discriminator}",
            Status = SellerStatus.Approved,
            CommissionRate = commissionRate
        };
        seller.SellerProfile = profile;
        return new Store
        {
            Seller = profile,
            Name = $"Store {discriminator} {suffix}",
            Slug = $"store-{discriminator}-{suffix}",
            Status = StoreStatus.Active
        };
    }

    private static Product CreateProduct(Store store, Category category, string sku, decimal price)
    {
        var product = new Product
        {
            Store = store,
            Sku = sku,
            Name = sku,
            Slug = sku.ToLowerInvariant(),
            Price = price,
            Currency = "ZAR",
            Status = ProductStatus.Active
        };
        product.Inventory = new InventoryItem
        {
            Product = product,
            OnHandQuantity = 10,
            ReorderLevel = 1
        };
        product.Categories.Add(new ProductCategory { Product = product, Category = category });
        return product;
    }

    private sealed record CheckoutSeed(Guid FirstProductId, Guid SecondProductId, string PromotionCode);
}

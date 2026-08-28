using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class AccountAndCartApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Addresses_AreValidatedOwnedAndMaintainOneDefault()
    {
        using var ownerClient = factory.CreateApiClient();
        var ownerToken = await RegisterAndLoginAsync(ownerClient, "address-owner");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var invalid = await ownerClient.PostAsJsonAsync("/api/account/addresses", new
        {
            type = "Shipping",
            recipientName = "Naledi Dlamini",
            line1 = "1 Market Street",
            city = "Johannesburg",
            province = "Invalid Province",
            postalCode = "2000",
            countryCode = "ZA"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var first = await ownerClient.PostAsJsonAsync("/api/account/addresses", AddressBody(
            "1 Market Street", "Johannesburg", "Gauteng", "2000", isDefault: false));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstId = firstBody.RootElement.GetProperty("id").GetGuid();
        Assert.True(firstBody.RootElement.GetProperty("isDefault").GetBoolean());

        var second = await ownerClient.PostAsJsonAsync("/api/account/addresses", AddressBody(
            "2 Long Street", "Cape Town", "Western Cape", "8001", isDefault: false));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondId = secondBody.RootElement.GetProperty("id").GetGuid();

        var makeSecondDefault = await ownerClient.PutAsJsonAsync($"/api/account/addresses/{secondId}", AddressBody(
            "2 Long Street", "Cape Town", "Western Cape", "8001", isDefault: true));
        Assert.Equal(HttpStatusCode.OK, makeSecondDefault.StatusCode);

        var list = await ownerClient.GetAsync("/api/account/addresses");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(2, listBody.RootElement.GetArrayLength());
        Assert.Equal(1, listBody.RootElement.EnumerateArray().Count(item => item.GetProperty("isDefault").GetBoolean()));
        Assert.Equal(secondId, listBody.RootElement[0].GetProperty("id").GetGuid());

        using var otherClient = factory.CreateApiClient();
        var otherToken = await RegisterAndLoginAsync(otherClient, "address-other");
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var crossCustomerDelete = await otherClient.DeleteAsync($"/api/account/addresses/{firstId}");
        Assert.Equal(HttpStatusCode.NotFound, crossCustomerDelete.StatusCode);

        var deleteDefault = await ownerClient.DeleteAsync($"/api/account/addresses/{secondId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteDefault.StatusCode);
        var remaining = await ownerClient.GetAsync("/api/account/addresses");
        using var remainingBody = JsonDocument.Parse(await remaining.Content.ReadAsStringAsync());
        Assert.True(remainingBody.RootElement[0].GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task Cart_EnforcesAvailabilityOwnershipAndServerSidePricing()
    {
        var product = await SeedCartProductAsync();
        using var ownerClient = factory.CreateApiClient();
        var ownerToken = await RegisterAndLoginAsync(ownerClient, "cart-owner");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var empty = await ownerClient.GetAsync("/api/cart");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyBody = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.Equal(0, emptyBody.RootElement.GetProperty("itemCount").GetInt32());

        var added = await ownerClient.PostAsJsonAsync("/api/cart/items", new
        {
            productId = product.ActiveProductId,
            quantity = 2
        });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        using var addedBody = JsonDocument.Parse(await added.Content.ReadAsStringAsync());
        var itemId = addedBody.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(500m, addedBody.RootElement.GetProperty("subtotal").GetDecimal());

        var merged = await ownerClient.PostAsJsonAsync("/api/cart/items", new
        {
            productId = product.ActiveProductId,
            quantity = 1
        });
        Assert.Equal(HttpStatusCode.Created, merged.StatusCode);
        using var mergedBody = JsonDocument.Parse(await merged.Content.ReadAsStringAsync());
        Assert.Equal(3, mergedBody.RootElement.GetProperty("items")[0].GetProperty("quantity").GetInt32());

        var tooMany = await ownerClient.PutAsJsonAsync($"/api/cart/items/{itemId}", new { quantity = 9 });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);

        var invalidQuantity = await ownerClient.PutAsJsonAsync($"/api/cart/items/{itemId}", new { quantity = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidQuantity.StatusCode);

        var inactive = await ownerClient.PostAsJsonAsync("/api/cart/items", new
        {
            productId = product.InactiveProductId,
            quantity = 1
        });
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var persisted = await dbContext.Products.SingleAsync(item => item.Id == product.ActiveProductId);
            persisted.Price = 275m;
            await dbContext.SaveChangesAsync();
        }

        var repriced = await ownerClient.GetAsync("/api/cart");
        using var repricedBody = JsonDocument.Parse(await repriced.Content.ReadAsStringAsync());
        Assert.Equal(825m, repricedBody.RootElement.GetProperty("subtotal").GetDecimal());

        using var otherClient = factory.CreateApiClient();
        var otherToken = await RegisterAndLoginAsync(otherClient, "cart-other");
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var crossCustomerUpdate = await otherClient.PutAsJsonAsync($"/api/cart/items/{itemId}", new { quantity = 1 });
        Assert.Equal(HttpStatusCode.NotFound, crossCustomerUpdate.StatusCode);

        var removed = await ownerClient.DeleteAsync($"/api/cart/items/{itemId}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var removedBody = JsonDocument.Parse(await removed.Content.ReadAsStringAsync());
        Assert.Equal(0, removedBody.RootElement.GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task AccountAndCart_RequireAuthentication()
    {
        using var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/addresses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/cart")).StatusCode);
    }

    private static object AddressBody(
        string line1,
        string city,
        string province,
        string postalCode,
        bool isDefault) => new
        {
            type = "Shipping",
            recipientName = "Naledi Dlamini",
            line1,
            line2 = (string?)null,
            city,
            province,
            postalCode,
            countryCode = "ZA",
            isDefault
        };

    private static async Task<string> RegisterAndLoginAsync(HttpClient client, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Naledi",
            lastName = "Dlamini"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<CartProductSeed> SeedCartProductAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var store = new Store
        {
            SellerId = Guid.NewGuid(),
            Name = $"Cart Seller {suffix}",
            Slug = $"cart-seller-{suffix}",
            Status = StoreStatus.Active
        };
        var category = new Category
        {
            Name = $"Cart Category {suffix}",
            Slug = $"cart-category-{suffix}",
            IsActive = true
        };
        var active = CreateProduct(store, category, $"CART-ACTIVE-{suffix}", $"cart-active-{suffix}",
            ProductStatus.Active, 250m);
        var inactive = CreateProduct(store, category, $"CART-INACTIVE-{suffix}", $"cart-inactive-{suffix}",
            ProductStatus.Inactive, 100m);
        dbContext.AddRange(store, category, active, inactive);
        await dbContext.SaveChangesAsync();
        return new CartProductSeed(active.Id, inactive.Id);
    }

    private static Product CreateProduct(
        Store store,
        Category category,
        string sku,
        string slug,
        ProductStatus status,
        decimal price)
    {
        var product = new Product
        {
            Store = store,
            Sku = sku,
            Name = sku,
            Slug = slug,
            Status = status,
            Price = price,
            Currency = "ZAR"
        };
        product.Inventory = new InventoryItem
        {
            Product = product,
            OnHandQuantity = 10,
            ReservedQuantity = 2,
            ReorderLevel = 1
        };
        product.Categories.Add(new ProductCategory { Product = product, Category = category });
        return product;
    }

    private sealed record CartProductSeed(Guid ActiveProductId, Guid InactiveProductId);
}

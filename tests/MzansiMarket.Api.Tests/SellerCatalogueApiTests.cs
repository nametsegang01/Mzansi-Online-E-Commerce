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

public sealed class SellerCatalogueApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Reseller_CanBuildDraftThenPublishAfterAdministratorApproval()
    {
        var seller = await RegisterSellerAsync("publish");
        var categoryId = await CreateCategoryAsync();
        using var sellerClient = Client(seller.Token);

        var store = await sellerClient.GetAsync("/api/seller/store");
        Assert.Equal(HttpStatusCode.OK, store.StatusCode);
        using (var body = JsonDocument.Parse(await store.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Pending", body.RootElement.GetProperty("sellerStatus").GetString());
            Assert.False(body.RootElement.GetProperty("canPublish").GetBoolean());
        }

        var create = await sellerClient.PostAsJsonAsync("/api/seller/products", ProductBody(categoryId, seller.Suffix));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var productId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Draft", created.RootElement.GetProperty("status").GetString());
        Assert.Equal(12, created.RootElement.GetProperty("onHandQuantity").GetInt32());

        var update = await sellerClient.PutAsJsonAsync($"/api/seller/products/{productId}", ProductBody(categoryId, seller.Suffix));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await sellerClient.PostAsync($"/api/seller/products/{productId}/publish", null)).StatusCode);
        using var publicClient = factory.CreateApiClient();
        Assert.Equal(0, await PublicCountAsync(publicClient, seller.Suffix));

        var administrator = await CreateAdministratorAsync();
        using var adminClient = Client(administrator);
        var approval = await adminClient.PostAsJsonAsync($"/api/admin/sellers/{seller.UserId}/decision",
            new { action = "Approve" });
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        var publish = await sellerClient.PostAsync($"/api/seller/products/{productId}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal(1, await PublicCountAsync(publicClient, seller.Suffix));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        Assert.Contains(await db.AuditEntries.Where(x => x.EntityId == productId.ToString()).ToArrayAsync(),
            x => x.Action == "SellerProductPublished");
        Assert.Contains(await db.InventoryTransactions.Where(x => x.ProductId == productId).ToArrayAsync(),
            x => x.Type == InventoryTransactionType.InitialStock && x.QuantityDelta == 12);
    }

    [Fact]
    public async Task Reseller_CannotReadOrChangeAnotherSellersProducts()
    {
        var owner = await RegisterSellerAsync("owner");
        var intruder = await RegisterSellerAsync("intruder");
        var categoryId = await CreateCategoryAsync();
        using var ownerClient = Client(owner.Token);
        var create = await ownerClient.PostAsJsonAsync("/api/seller/products", ProductBody(categoryId, owner.Suffix));
        using var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var productId = body.RootElement.GetProperty("id").GetGuid();

        using var intruderClient = Client(intruder.Token);
        var list = await intruderClient.GetFromJsonAsync<JsonElement[]>("/api/seller/products");
        Assert.DoesNotContain(list!, item => item.GetProperty("id").GetGuid() == productId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruderClient.PutAsJsonAsync($"/api/seller/products/{productId}", ProductBody(categoryId, intruder.Suffix))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruderClient.DeleteAsync($"/api/seller/products/{productId}")).StatusCode);
    }

    [Fact]
    public async Task Inventory_ProtectsReservedStockAndRecordsAdjustments()
    {
        var seller = await RegisterSellerAsync("inventory");
        var categoryId = await CreateCategoryAsync();
        using var client = Client(seller.Token);
        var create = await client.PostAsJsonAsync("/api/seller/products", ProductBody(categoryId, seller.Suffix));
        using var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var productId = body.RootElement.GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var inventory = await db.InventoryItems.SingleAsync(x => x.ProductId == productId);
            inventory.ReservedQuantity = 3;
            await db.SaveChangesAsync();
        }

        var invalid = await client.PutAsJsonAsync($"/api/seller/products/{productId}/inventory",
            new { onHandQuantity = 2, reorderLevel = 2, reason = "Invalid reserved reduction" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var valid = await client.PutAsJsonAsync($"/api/seller/products/{productId}/inventory",
            new { onHandQuantity = 20, reorderLevel = 4, reason = "New reseller delivery" });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var validBody = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        Assert.Equal(17, validBody.RootElement.GetProperty("availableQuantity").GetInt32());
    }

    [Fact]
    public async Task ProductValidation_RequiresActiveCategoryHttpsImageAndAltText()
    {
        var seller = await RegisterSellerAsync("validation");
        using var client = Client(seller.Token);
        var response = await client.PostAsJsonAsync("/api/seller/products", new
        {
            sku = "BAD",
            name = "Bad product",
            slug = "bad-product",
            price = 10,
            categoryIds = Array.Empty<Guid>(),
            imageUrl = "http://images.example.test/item.jpg",
            initialStock = 1,
            reorderLevel = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("CategoryIds", out _));
        Assert.True(errors.TryGetProperty("ImageUrl", out _));
        Assert.True(errors.TryGetProperty("ImageAltText", out _));
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<SellerSetup> RegisterSellerAsync(string prefix)
    {
        using var client = factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N"); var email = $"{prefix}-{suffix}@example.test";
        var registration = await client.PostAsJsonAsync("/api/auth/register/seller", new
        {
            email,
            password = "LocalOnly!2345",
            firstName = "Lerato",
            lastName = "Mokoena",
            tradingName = $"Reseller {suffix}",
            storeSlug = $"reseller-{suffix}",
            supportEmail = email
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registrationBody = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        var userId = registrationBody.RootElement.GetProperty("userId").GetGuid();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "LocalOnly!2345" });
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return new SellerSetup(userId, suffix, loginBody.RootElement.GetProperty("accessToken").GetString()!);
    }

    private async Task<Guid> CreateCategoryAsync()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N"); var category = new Category { Name = $"Reseller {suffix}", Slug = $"reseller-{suffix}" };
        db.Categories.Add(category); await db.SaveChangesAsync(); return category.Id;
    }

    private async Task<string> CreateAdministratorAsync()
    {
        using var scope = factory.Services.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var suffix = Guid.NewGuid().ToString("N"); var email = $"admin-{suffix}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = "Test Admin", Status = AccountStatus.Active };
        Assert.True((await manager.CreateAsync(user, "LocalOnly!2345")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, AppRoles.SystemAdministrator)).Succeeded);
        using var client = factory.CreateApiClient(); var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "LocalOnly!2345" });
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync()); return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static object ProductBody(Guid categoryId, string suffix) => new
    {
        sku = $"RS-{suffix}",
        name = $"Local reseller item {suffix}",
        slug = $"local-item-{suffix}",
        description = "A fictional local reseller product.",
        price = 349.95m,
        categoryIds = new[] { categoryId },
        imageUrl = "https://images.example.test/reseller-item.jpg",
        imageAltText = "A local reseller product",
        initialStock = 12,
        reorderLevel = 3
    };

    private static async Task<int> PublicCountAsync(HttpClient client, string suffix)
    {
        using var body = JsonDocument.Parse(await (await client.GetAsync($"/api/products?search={suffix}")).Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("totalCount").GetInt32();
    }

    private sealed record SellerSetup(Guid UserId, string Suffix, string Token);
}

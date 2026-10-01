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

public sealed class PromotionAdministrationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(AppRoles.ProductAdministrator)]
    [InlineData(AppRoles.SystemAdministrator)]
    public async Task AuthorizedStaff_CanCreateUpdateAndFilterPromotions(string role)
    {
        var session = await CreateAuthenticatedClientWithRoleAsync(role);
        using var client = session.Client;
        var code = $"spring-{Guid.NewGuid():N}";
        var productId = await CreateProductAsync();
        var create = await client.PostAsJsonAsync("/api/admin/promotions", PromotionBody(
            code, status: "Draft", productIds: [productId]));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createdBody = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var promotionId = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(code.ToUpperInvariant(), createdBody.RootElement.GetProperty("code").GetString());
        Assert.Contains(productId, createdBody.RootElement.GetProperty("productIds").EnumerateArray()
            .Select(item => item.GetGuid()));

        var draftList = await client.GetFromJsonAsync<JsonElement[]>("/api/admin/promotions?status=Draft");
        Assert.Contains(draftList!, item => item.GetProperty("id").GetGuid() == promotionId);
        Assert.DoesNotContain(draftList!, item => item.GetProperty("status").GetString() != "Draft");

        var updateBody = PromotionBody(code, status: "Active", productIds: [productId]);
        updateBody["name"] = "Updated promotion";
        var update = await client.PutAsJsonAsync($"/api/admin/promotions/{promotionId}", updateBody);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updatedBody = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal("Updated promotion", updatedBody.RootElement.GetProperty("name").GetString());
        Assert.Equal("Active", updatedBody.RootElement.GetProperty("status").GetString());

        var activeList = await client.GetFromJsonAsync<JsonElement[]>("/api/admin/promotions?status=Active");
        Assert.Contains(activeList!, item => item.GetProperty("id").GetGuid() == promotionId);

        await AssertValidationFieldAsync(
            await client.GetAsync("/api/admin/promotions?status=active"), "Status");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var auditEntries = await dbContext.AuditEntries
            .Where(entry => entry.EntityId == promotionId.ToString())
            .ToArrayAsync();
        Assert.Contains(auditEntries, entry => entry.Action == "PromotionCreated");
        Assert.Contains(auditEntries, entry => entry.Action == "PromotionUpdated");
    }

    [Fact]
    public async Task PromotionValidation_EnforcesCodesEnumsValuesDatesAndReferences()
    {
        var session = await CreateAuthenticatedClientWithRoleAsync(AppRoles.ProductAdministrator);
        using var client = session.Client;

        var originalCode = $"validate-{Guid.NewGuid():N}";
        var original = await client.PostAsJsonAsync("/api/admin/promotions", PromotionBody(originalCode));
        Assert.Equal(HttpStatusCode.Created, original.StatusCode);

        var duplicateCode = await PostInvalidAsync(client, PromotionBody(originalCode.ToUpperInvariant()));
        await AssertValidationFieldAsync(duplicateCode, "Code");

        var badTypeBody = PromotionBody($"bad-type-{Guid.NewGuid():N}");
        badTypeBody["type"] = "percentage";
        await AssertValidationFieldAsync(await PostInvalidAsync(client, badTypeBody), "Type");

        var badStatusBody = PromotionBody($"bad-status-{Guid.NewGuid():N}");
        badStatusBody["status"] = "active";
        await AssertValidationFieldAsync(await PostInvalidAsync(client, badStatusBody), "Status");

        foreach (var percentage in new[] { -0.01m, 100.01m })
        {
            var invalidPercentage = PromotionBody($"bad-percent-{Guid.NewGuid():N}");
            invalidPercentage["value"] = percentage;
            await AssertValidationFieldAsync(await PostInvalidAsync(client, invalidPercentage), "Value");
        }

        var zeroPercentage = await client.PostAsJsonAsync("/api/admin/promotions",
            PromotionBody($"zero-percent-{Guid.NewGuid():N}", value: 0m));
        Assert.Equal(HttpStatusCode.Created, zeroPercentage.StatusCode);

        foreach (var fixedAmount in new[] { 0m, -0.01m })
        {
            var invalidFixedAmount = PromotionBody(
                $"invalid-fixed-{Guid.NewGuid():N}", type: "FixedAmount", value: fixedAmount);
            await AssertValidationFieldAsync(await PostInvalidAsync(client, invalidFixedAmount), "Value");
        }
        var validFixedAmount = await client.PostAsJsonAsync("/api/admin/promotions",
            PromotionBody($"valid-fixed-{Guid.NewGuid():N}", type: "FixedAmount", value: 12.50m));
        Assert.Equal(HttpStatusCode.Created, validFixedAmount.StatusCode);

        var negativeMinimum = PromotionBody($"negative-minimum-{Guid.NewGuid():N}");
        negativeMinimum["minimumOrderAmount"] = -0.01m;
        await AssertValidationFieldAsync(await PostInvalidAsync(client, negativeMinimum), "MinimumOrderAmount");

        var negativeLimit = PromotionBody($"negative-limit-{Guid.NewGuid():N}");
        negativeLimit["usageLimit"] = -1;
        await AssertValidationFieldAsync(await PostInvalidAsync(client, negativeLimit), "UsageLimit");

        var invalidDateRange = PromotionBody($"invalid-dates-{Guid.NewGuid():N}");
        invalidDateRange["endsAt"] = ((DateTimeOffset)invalidDateRange["startsAt"]!).AddTicks(-1);
        await AssertValidationFieldAsync(await PostInvalidAsync(client, invalidDateRange), "EndsAt");

        var missingStart = PromotionBody($"missing-start-{Guid.NewGuid():N}");
        missingStart["startsAt"] = default(DateTimeOffset);
        await AssertValidationFieldAsync(await PostInvalidAsync(client, missingStart), "StartsAt");

        var missingEnd = PromotionBody($"missing-end-{Guid.NewGuid():N}");
        missingEnd["endsAt"] = default(DateTimeOffset);
        await AssertValidationFieldAsync(await PostInvalidAsync(client, missingEnd), "EndsAt");

        var missingProduct = PromotionBody($"missing-product-{Guid.NewGuid():N}");
        missingProduct["productIds"] = new[] { Guid.NewGuid() };
        await AssertValidationFieldAsync(await PostInvalidAsync(client, missingProduct), "ProductIds");
    }

    [Fact]
    public async Task Update_RejectsRenamingUsedCodeAndReducingUsageLimitBelowCurrentCount()
    {
        var session = await CreateAuthenticatedClientWithRoleAsync(AppRoles.SystemAdministrator);
        using var client = session.Client;
        var code = $"used-{Guid.NewGuid():N}".ToUpperInvariant();
        var promotionResponse = await client.PostAsJsonAsync("/api/admin/promotions", PromotionBody(code, usageLimit: 3));
        Assert.Equal(HttpStatusCode.Created, promotionResponse.StatusCode);
        using var promotionBody = JsonDocument.Parse(await promotionResponse.Content.ReadAsStringAsync());
        var promotionId = promotionBody.RootElement.GetProperty("id").GetGuid();
        await SeedOrderUseAsync(session.UserId, code);

        var renamed = PromotionBody($"renamed-{Guid.NewGuid():N}", usageLimit: 3);
        await AssertValidationFieldAsync(
            await client.PutAsJsonAsync($"/api/admin/promotions/{promotionId}", renamed), "Code");

        var reducedLimit = PromotionBody(code, usageLimit: 0);
        await AssertValidationFieldAsync(
            await client.PutAsJsonAsync($"/api/admin/promotions/{promotionId}", reducedLimit), "UsageLimit");
    }

    [Theory]
    [InlineData(AppRoles.Customer)]
    [InlineData(AppRoles.Seller)]
    [InlineData(AppRoles.FulfilmentEmployee)]
    [InlineData(AppRoles.BusinessManager)]
    public async Task OtherAuthenticatedRoles_CannotManagePromotions(string role)
    {
        var session = await CreateAuthenticatedClientWithRoleAsync(role);
        using var client = session.Client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/promotions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/admin/promotions", PromotionBody($"denied-{Guid.NewGuid():N}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/admin/promotions/{Guid.NewGuid()}", PromotionBody($"denied-{Guid.NewGuid():N}"))).StatusCode);
    }

    [Fact]
    public async Task AnonymousUsers_CannotManagePromotions()
    {
        using var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/promotions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/admin/promotions", PromotionBody($"anonymous-{Guid.NewGuid():N}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync($"/api/admin/promotions/{Guid.NewGuid()}", PromotionBody($"anonymous-{Guid.NewGuid():N}"))).StatusCode);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientWithRoleAsync(string role)
    {
        var client = factory.CreateApiClient();
        var email = $"promotion-role-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Promotion",
            lastName = "Tester"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registrationBody = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        var userId = registrationBody.RootElement.GetProperty("userId").GetGuid();

        if (role != AppRoles.Customer)
        {
            using var scope = factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            Assert.NotNull(user);
            var result = await userManager.AddToRoleAsync(user, role);
            Assert.True(result.Succeeded);
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("accessToken").GetString());
        return (client, userId);
    }

    private async Task<Guid> CreateProductAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var sellerUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"promotion-product-{suffix}@example.test",
            Email = $"promotion-product-{suffix}@example.test",
            DisplayName = "Promotion product seller",
            Status = AccountStatus.Active
        };
        var seller = new SellerProfile
        {
            User = sellerUser,
            TradingName = $"Promotion seller {suffix}",
            Status = SellerStatus.Approved,
            CommissionRate = 0.1m
        };
        var store = new Store
        {
            Seller = seller,
            Name = $"Promotion store {suffix}",
            Slug = $"promotion-store-{suffix}",
            Status = StoreStatus.Active
        };
        var category = new Category { Name = $"Promotion category {suffix}", Slug = $"promotion-category-{suffix}" };
        var product = new Product
        {
            Store = store,
            Sku = $"PROMOTION-{suffix}",
            Name = "Promotion test product",
            Slug = $"promotion-product-{suffix}",
            Price = 100m,
            Status = ProductStatus.Active,
            Inventory = new InventoryItem { OnHandQuantity = 10, ReorderLevel = 1 }
        };
        product.Inventory.Product = product;
        product.Categories.Add(new ProductCategory { Product = product, Category = category });
        dbContext.AddRange(sellerUser, category, store, product);
        await dbContext.SaveChangesAsync();
        return product.Id;
    }

    private async Task SeedOrderUseAsync(Guid customerId, string code)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        dbContext.Orders.Add(new Order
        {
            OrderNumber = $"USED-{suffix}",
            CustomerId = customerId,
            PromotionCode = code,
            Status = OrderStatus.Paid,
            Subtotal = 100m,
            GrandTotal = 100m,
            PlacedAt = DateTimeOffset.UtcNow,
            PaidAt = DateTimeOffset.UtcNow,
            ShippingAddress = new OrderAddress
            {
                RecipientName = "Promotion test customer",
                Line1 = "1 Test Road",
                City = "Johannesburg",
                Province = "Gauteng",
                PostalCode = "2000",
                CountryCode = "ZA"
            }
        });
        await dbContext.SaveChangesAsync();
    }

    private static Dictionary<string, object?> PromotionBody(
        string code,
        string type = "Percentage",
        string status = "Draft",
        decimal value = 10m,
        decimal? minimumOrderAmount = null,
        int? usageLimit = null,
        Guid[]? productIds = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Dictionary<string, object?>
        {
            ["code"] = code,
            ["name"] = "Promotion test",
            ["type"] = type,
            ["status"] = status,
            ["value"] = value,
            ["minimumOrderAmount"] = minimumOrderAmount,
            ["usageLimit"] = usageLimit,
            ["startsAt"] = now.AddDays(-1),
            ["endsAt"] = now.AddDays(1),
            ["productIds"] = productIds ?? []
        };
    }

    private static async Task<HttpResponseMessage> PostInvalidAsync(
        HttpClient client,
        Dictionary<string, object?> body) =>
        await client.PostAsJsonAsync("/api/admin/promotions", body);

    private static async Task AssertValidationFieldAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }
}
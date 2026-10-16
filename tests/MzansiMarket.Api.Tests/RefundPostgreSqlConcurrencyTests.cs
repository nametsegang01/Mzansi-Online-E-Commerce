using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;
using Testcontainers.PostgreSql;

namespace MzansiMarket.Api.Tests;

[CollectionDefinition(Name)]
public sealed class RefundPostgreSqlCollection : ICollectionFixture<RefundPostgreSqlFixture>
{
    public const string Name = "Refund PostgreSQL integration";
}

public sealed class RefundPostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;

    public string? ConnectionString => container?.GetConnectionString();
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            container = new PostgreSqlBuilder()
                .WithImage("postgres:18-alpine")
                .Build();
            await container.StartAsync();
        }
        catch (Exception exception)
        {
            UnavailableReason = $"Disposable PostgreSQL test container unavailable: {exception.Message}";
            if (container is not null) await container.DisposeAsync();
            container = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
    }
}

[Collection(RefundPostgreSqlCollection.Name)]
public sealed class RefundPostgreSqlConcurrencyTests(RefundPostgreSqlFixture fixture)
    : IClassFixture<RefundPostgreSqlFixture>
{
    [SkippableFact]
    public async Task SimultaneousDecisionsOnOneReturn_ExactlyOneWinsAndCreatesOneRefund()
    {
        using var factory = CreateFactory();
        var firstReviewer = await CreateReviewerAsync(factory);
        var secondReviewer = await CreateReviewerAsync(factory);
        var seed = await SeedReturnsAsync(factory, firstReviewer.UserId, 2, 100m, (1, 50m));

        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        Authenticate(firstClient, firstReviewer.AccessToken);
        Authenticate(secondClient, secondReviewer.AccessToken);
        var route = $"/api/admin/refunds/{seed.ReturnRequestIds[0]}/decision";
        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync(route, new { action = "Approve" }),
            secondClient.PostAsJsonAsync(route, new { action = "Approve" }));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        Assert.Equal(1, await dbContext.RefundRecords.CountAsync(item =>
            item.ReturnRequestId == seed.ReturnRequestIds[0]));
    }

    [SkippableFact]
    public async Task SimultaneousReturnsOnOneOrderItem_NeverExceedPaidLineTotal()
    {
        using var factory = CreateFactory();
        var reviewer = await CreateReviewerAsync(factory);
        var seed = await SeedReturnsAsync(factory, reviewer.UserId, 2, 100m, (1, 50m), (1, 50m));
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        Authenticate(firstClient, reviewer.AccessToken);
        Authenticate(secondClient, reviewer.AccessToken);

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync($"/api/admin/refunds/{seed.ReturnRequestIds[0]}/decision", new { action = "Approve" }),
            secondClient.PostAsJsonAsync($"/api/admin/refunds/{seed.ReturnRequestIds[1]}/decision", new { action = "Approve" }));
        Assert.All(responses, response => Assert.Contains(response.StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var totalRefunded = await dbContext.RefundRecords
            .Where(refund => refund.ReturnRequest.OrderItemId == seed.OrderItemId
                && (refund.Status == RefundStatus.Pending
                    || refund.Status == RefundStatus.Submitted
                    || refund.Status == RefundStatus.Paid))
            .SumAsync(refund => refund.Amount);
        Assert.True(totalRefunded <= 100m, $"Total refunds {totalRefunded} exceeded the paid line total.");
    }

    [SkippableFact]
    public async Task AuditFailure_RollsBackReturnStatusAndRefundRecord()
    {
        using var factory = CreateFactory();
        var reviewer = await CreateReviewerAsync(factory);
        var seed = await SeedReturnsAsync(factory, reviewer.UserId, 2, 100m, (1, 50m));

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE audit.\"AuditEntries\" ADD CONSTRAINT \"CK_Test_RejectRefundAudit\" CHECK (\"Action\" <> 'RefundApproved')");
        }

        using var client = factory.CreateClient();
        Authenticate(client, reviewer.AccessToken);
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/admin/refunds/{seed.ReturnRequestIds[0]}/decision", new { action = "Approve" });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE audit.\"AuditEntries\" DROP CONSTRAINT \"CK_Test_RejectRefundAudit\"");
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var returnRequest = await dbContext.ReturnRequests.SingleAsync(item => item.Id == seed.ReturnRequestIds[0]);
            Assert.Equal(ReturnStatus.Requested, returnRequest.Status);
            Assert.Equal(0, await dbContext.RefundRecords.CountAsync(item =>
                item.ReturnRequestId == returnRequest.Id));
            Assert.Equal(0, await dbContext.AuditEntries.CountAsync(item =>
                item.EntityId == returnRequest.Id.ToString() && item.Action == "RefundApproved"));
        }
    }

    private RefundPostgreSqlApiFactory CreateFactory()
    {
        Skip.If(string.IsNullOrWhiteSpace(fixture.ConnectionString),
            fixture.UnavailableReason ?? "Docker is unavailable for the isolated PostgreSQL tests.");
        return new RefundPostgreSqlApiFactory(fixture.ConnectionString!);
    }

    private static async Task<Reviewer> CreateReviewerAsync(RefundPostgreSqlApiFactory factory)
    {
        using var client = factory.CreateClient();
        var email = $"refund-manager-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Refund",
            lastName = "Manager"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registrationBody = System.Text.Json.JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        var userId = registrationBody.RootElement.GetProperty("userId").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            Assert.NotNull(user);
            Assert.True((await userManager.AddToRoleAsync(user, AppRoles.BusinessManager)).Succeeded);
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return new Reviewer(userId, loginBody.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static async Task<SeededReturn> SeedReturnsAsync(
        RefundPostgreSqlApiFactory factory,
        Guid customerId,
        int itemQuantity,
        decimal lineTotal,
        params (int Quantity, decimal RefundAmount)[] returns)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var sellerUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"refund-seller-{suffix}@example.test",
            Email = $"refund-seller-{suffix}@example.test",
            DisplayName = "Refund Test Seller",
            Status = AccountStatus.Active
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await userManager.CreateAsync(sellerUser, "LocalOnly!2345")).Succeeded);
        var seller = new SellerProfile
        {
            User = sellerUser,
            TradingName = $"Refund Seller {suffix}",
            Status = SellerStatus.Approved,
            CommissionRate = 0.1m
        };
        var store = new Store
        {
            Seller = seller,
            Name = $"Refund Store {suffix}",
            Slug = $"refund-store-{suffix}",
            Status = StoreStatus.Active
        };
        var category = new Category { Name = $"Refund category {suffix}", Slug = $"refund-category-{suffix}" };
        var product = new Product
        {
            Store = store,
            Sku = $"REFUND-{suffix}",
            Name = "Refund concurrency product",
            Slug = $"refund-product-{suffix}",
            Price = lineTotal / itemQuantity,
            Status = ProductStatus.Active,
            Inventory = new InventoryItem { OnHandQuantity = itemQuantity, ReorderLevel = 1 }
        };
        product.Inventory.Product = product;
        product.Categories.Add(new ProductCategory { Product = product, Category = category });
        var customer = await userManager.FindByIdAsync(customerId.ToString());
        Assert.NotNull(customer);
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            OrderNumber = $"REF-{suffix[..20]}",
            Customer = customer,
            Status = OrderStatus.Delivered,
            Subtotal = lineTotal,
            GrandTotal = lineTotal,
            PaidAt = now,
            PlacedAt = now,
            ShippingAddress = new OrderAddress
            {
                RecipientName = customer.DisplayName,
                Line1 = "1 Refund Test Road",
                City = "Johannesburg",
                Province = "Gauteng",
                PostalCode = "2000",
                CountryCode = "ZA"
            }
        };
        var sellerOrder = new SellerOrder
        {
            Order = order,
            Seller = seller,
            Store = store,
            Status = SellerOrderStatus.Delivered,
            Subtotal = lineTotal,
            SellerNetAmount = lineTotal
        };
        var orderItem = new OrderItem
        {
            SellerOrder = sellerOrder,
            Product = product,
            SkuSnapshot = product.Sku,
            ProductNameSnapshot = product.Name,
            Quantity = itemQuantity,
            UnitPrice = product.Price,
            LineTotal = lineTotal
        };
        var payment = new PaymentRecord
        {
            Order = order,
            Provider = "Sandbox",
            ProviderReference = $"REFUND-PAY-{suffix}",
            PaymentMethodType = "TestWallet",
            Status = PaymentStatus.Paid,
            Amount = lineTotal,
            PaidAt = now
        };
        order.SellerOrders.Add(sellerOrder);
        sellerOrder.Items.Add(orderItem);
        order.Payments.Add(payment);
        var requests = returns.Select(item => new ReturnRequest
        {
            OrderItem = orderItem,
            Customer = customer,
            Quantity = item.Quantity,
            Reason = "Test return reason",
            Status = ReturnStatus.Requested,
            RefundAmount = item.RefundAmount,
            RequestedAt = now
        }).ToArray();
        foreach (var request in requests) orderItem.ReturnRequests.Add(request);

        dbContext.AddRange(sellerUser, category, store, product, order, payment);
        dbContext.ReturnRequests.AddRange(requests);
        await dbContext.SaveChangesAsync();
        return new SeededReturn(orderItem.Id, requests.Select(request => request.Id).ToArray());
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private sealed record Reviewer(Guid UserId, string AccessToken);
    private sealed record SeededReturn(Guid OrderItemId, IReadOnlyList<Guid> ReturnRequestIds);
}

internal sealed class RefundPostgreSqlApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Database:ApplyMigrations"] = "true",
                ["RateLimiting:AuthenticationPermitLimit"] = "100",
                ["SandboxPayments:WebhookSecret"] = "refund-postgres-test-only",
                ["BootstrapAdministrator:Email"] = string.Empty,
                ["BootstrapAdministrator:Password"] = string.Empty
            }));
    }
}
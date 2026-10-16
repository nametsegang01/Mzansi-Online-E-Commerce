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

public sealed class RefundAdministrationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void ReturnStatus_IsConcurrencyToken()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var property = dbContext.Model.FindEntityType(typeof(ReturnRequest))!
            .FindProperty(nameof(ReturnRequest.Status));

        Assert.True(property!.IsConcurrencyToken);
    }

    [Fact]
    public async Task Queue_FiltersInclusivelyByUtcDateStoreAndStatusAndPaginates()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-2);
        var first = await SeedReturnsAsync(2, 100m,
        [
            new ReturnSeed(1, 50m, ReturnStatus.Requested, from),
            new ReturnSeed(1, 50m, ReturnStatus.Requested, from.AddHours(1))
        ]);
        await SeedReturnsAsync(1, 20m,
        [new ReturnSeed(1, 20m, ReturnStatus.Requested, from.AddMinutes(30))]);

        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);
        var fromQuery = Uri.EscapeDataString(from.ToString("O"));
        var toQuery = Uri.EscapeDataString(from.AddHours(2).ToString("O"));
        var firstPage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/refunds?from={fromQuery}&to={toQuery}&storeId={first.StoreId}&status=Requested&page=1&pageSize=1");

        Assert.Equal(2, firstPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, firstPage.GetProperty("pageSize").GetInt32());
        var firstItem = firstPage.GetProperty("items")[0];
        Assert.Equal(first.ReturnRequestIds[0], firstItem.GetProperty("returnRequestId").GetGuid());
        Assert.Equal("Naledi Refund Customer", firstItem.GetProperty("customerDisplayName").GetString());
        Assert.EndsWith("@example.test", firstItem.GetProperty("customerEmail").GetString());
        Assert.False(firstItem.TryGetProperty("mobileNumber", out _));

        var secondPage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/refunds?from={fromQuery}&to={toQuery}&storeId={first.StoreId}&status=Requested&page=2&pageSize=1");
        Assert.Equal(first.ReturnRequestIds[1], secondPage.GetProperty("items")[0]
            .GetProperty("returnRequestId").GetGuid());

        var invalid = await client.GetAsync("/api/admin/refunds?status=requested&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Approve_CreatesPendingRefundAndAuditWithoutProviderCall()
    {
        var seeded = await SeedReturnsAsync(2, 100m,
            [new ReturnSeed(1, 50m, ReturnStatus.Requested, DateTimeOffset.UtcNow)]);
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);

        var defaultQueue = await client.GetFromJsonAsync<JsonElement>("/api/admin/refunds");
        Assert.Contains(defaultQueue.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("returnRequestId").GetGuid() == seeded.ReturnRequestIds[0]);

        var response = await client.PostAsJsonAsync(
            $"/api/admin/refunds/{seeded.ReturnRequestIds[0]}/decision",
            new { action = "Approve", reason = "Receipt inspected" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Approved", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(50m, body.RootElement.GetProperty("refundRecord").GetProperty("amount").GetDecimal());
        Assert.Equal("Pending", body.RootElement.GetProperty("refundRecord").GetProperty("status").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var refund = await dbContext.RefundRecords.SingleAsync(record =>
            record.ReturnRequestId == seeded.ReturnRequestIds[0]);
        Assert.Equal(RefundStatus.Pending, refund.Status);
        Assert.Equal(50m, refund.Amount);
        Assert.Equal(0, await dbContext.PaymentProviderEvents.CountAsync());
        var audit = await dbContext.AuditEntries.SingleAsync(entry =>
            entry.EntityId == seeded.ReturnRequestIds[0].ToString() && entry.Action == "RefundApproved");
        Assert.Contains("Receipt inspected", audit.ChangesJson);
    }

    [Fact]
    public async Task Reject_RequiresReasonAuditsItAndSecondDecisionConflicts()
    {
        var seeded = await SeedReturnsAsync(1, 100m,
            [new ReturnSeed(1, 100m, ReturnStatus.Requested, DateTimeOffset.UtcNow)]);
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.SystemAdministrator);
        var path = $"/api/admin/refunds/{seeded.ReturnRequestIds[0]}/decision";

        var missingReason = await client.PostAsJsonAsync(path, new { action = "Reject" });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        using (var errors = JsonDocument.Parse(await missingReason.Content.ReadAsStringAsync()))
        {
            Assert.True(errors.RootElement.GetProperty("errors").TryGetProperty("Reason", out _));
        }

        var rejected = await client.PostAsJsonAsync(path,
            new { action = "Reject", reason = "Evidence does not support the claim" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var duplicate = await client.PostAsJsonAsync(path,
            new { action = "Approve", reason = "Second attempt" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var request = await dbContext.ReturnRequests.SingleAsync(item => item.Id == seeded.ReturnRequestIds[0]);
        Assert.Equal(ReturnStatus.Rejected, request.Status);
        Assert.Empty(await dbContext.RefundRecords.Where(item => item.ReturnRequestId == request.Id).ToArrayAsync());
        var audit = await dbContext.AuditEntries.SingleAsync(entry =>
            entry.EntityId == request.Id.ToString() && entry.Action == "RefundRejected");
        Assert.Contains("Evidence does not support the claim", audit.ChangesJson);
    }

    [Fact]
    public async Task Approve_RejectsInvalidActionResolvedReturnAndAmountAboveCap()
    {
        var seeded = await SeedReturnsAsync(2, 100m,
        [
            new ReturnSeed(1, 50m, ReturnStatus.Requested, DateTimeOffset.UtcNow),
            new ReturnSeed(1, 50m, ReturnStatus.Approved, DateTimeOffset.UtcNow)
        ]);
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);

        var invalidAction = await client.PostAsJsonAsync(
            $"/api/admin/refunds/{seeded.ReturnRequestIds[0]}/decision",
            new { action = "Refund", reason = "Invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidAction.StatusCode);

        var resolved = await client.PostAsJsonAsync(
            $"/api/admin/refunds/{seeded.ReturnRequestIds[1]}/decision",
            new { action = "Reject", reason = "Already resolved" });
        Assert.Equal(HttpStatusCode.Conflict, resolved.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var pending = await dbContext.ReturnRequests.SingleAsync(item => item.Id == seeded.ReturnRequestIds[0]);
            pending.RefundAmount = 50.01m;
            await dbContext.SaveChangesAsync();
        }

        var overCap = await client.PostAsJsonAsync(
            $"/api/admin/refunds/{seeded.ReturnRequestIds[0]}/decision",
            new { action = "Approve" });
        Assert.Equal(HttpStatusCode.BadRequest, overCap.StatusCode);
        using var overCapBody = JsonDocument.Parse(await overCap.Content.ReadAsStringAsync());
        Assert.True(overCapBody.RootElement.GetProperty("errors").TryGetProperty("RefundAmount", out _));
    }

    [Fact]
    public async Task Cap_UsesPendingSubmittedAndPaidRefundsButIgnoresFailedAndCancelled()
    {
        var seeded = await SeedReturnsAsync(10, 100m,
            [new ReturnSeed(7, 60.01m, ReturnStatus.Requested, DateTimeOffset.UtcNow)]);
        await SeedPriorRefundsAsync(seeded,
        [
            new PriorRefund(10m, RefundStatus.Pending),
            new PriorRefund(10m, RefundStatus.Submitted),
            new PriorRefund(20m, RefundStatus.Paid),
            new PriorRefund(50m, RefundStatus.Failed),
            new PriorRefund(50m, RefundStatus.Cancelled)
        ]);
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);

        var queueResponse = await client.GetAsync(
            $"/api/admin/refunds?storeId={seeded.StoreId}&status=Requested&page=1&pageSize=100");
        var queueContent = await queueResponse.Content.ReadAsStringAsync();
        Assert.True(queueResponse.IsSuccessStatusCode, queueContent);
        using var queueDocument = JsonDocument.Parse(queueContent);
        var queue = queueDocument.RootElement;
        Assert.Equal(60m, queue.GetProperty("items")[0].GetProperty("maximumRefundAmount").GetDecimal());

        var overCap = await client.PostAsJsonAsync(
            $"/api/admin/refunds/{seeded.ReturnRequestIds[0]}/decision", new { action = "Approve" });
        Assert.Equal(HttpStatusCode.BadRequest, overCap.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var request = await dbContext.ReturnRequests.SingleAsync(item => item.Id == seeded.ReturnRequestIds[0]);
            request.RefundAmount = 60m;
            await dbContext.SaveChangesAsync();
        }

        var atCap = await client.PostAsJsonAsync(
            $"/api/admin/refunds/{seeded.ReturnRequestIds[0]}/decision", new { action = "Approve" });
        Assert.Equal(HttpStatusCode.OK, atCap.StatusCode);
    }

    [Theory]
    [InlineData(AppRoles.Customer)]
    [InlineData(AppRoles.Seller)]
    [InlineData(AppRoles.ProductAdministrator)]
    [InlineData(AppRoles.FulfilmentEmployee)]
    public async Task OtherRoles_CannotAccessRefundAdministration(string role)
    {
        using var client = await CreateAuthenticatedClientWithRoleAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/refunds")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/admin/refunds/{Guid.NewGuid()}/decision",
                new { action = "Reject", reason = "Denied" })).StatusCode);
    }

    [Fact]
    public async Task AnonymousUsers_CannotAccessRefundAdministration()
    {
        using var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/refunds")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/api/admin/refunds/{Guid.NewGuid()}/decision",
                new { action = "Reject", reason = "Denied" })).StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientWithRoleAsync(string role)
    {
        var client = factory.CreateApiClient();
        var email = $"refund-role-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Refund",
            lastName = "Reviewer"
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
        return client;
    }

    private async Task<SeededReturns> SeedReturnsAsync(
        int itemQuantity,
        decimal lineTotal,
        IReadOnlyCollection<ReturnSeed> returns)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var customer = TestUser($"refund-customer-{suffix}@example.test", "Naledi Refund Customer");
        var sellerUser = TestUser($"refund-seller-{suffix}@example.test", "Refund Seller");
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
            Name = "Refund test product",
            Slug = $"refund-product-{suffix}",
            Price = lineTotal / itemQuantity,
            Status = ProductStatus.Active,
            Inventory = new InventoryItem { OnHandQuantity = itemQuantity, ReorderLevel = 1 }
        };
        product.Inventory.Product = product;
        product.Categories.Add(new ProductCategory { Product = product, Category = category });
        var order = new Order
        {
            OrderNumber = $"REF-{suffix[..20]}",
            Customer = customer,
            Status = OrderStatus.Delivered,
            Subtotal = lineTotal,
            GrandTotal = lineTotal,
            PaidAt = DateTimeOffset.UtcNow,
            PlacedAt = DateTimeOffset.UtcNow,
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
            PaidAt = order.PaidAt
        };
        order.SellerOrders.Add(sellerOrder);
        sellerOrder.Items.Add(orderItem);
        order.Payments.Add(payment);
        var returnRequests = returns.Select(seed => new ReturnRequest
        {
            OrderItem = orderItem,
            Customer = customer,
            Quantity = seed.Quantity,
            Reason = "Test return reason",
            Status = seed.Status,
            RefundAmount = seed.Amount,
            RequestedAt = seed.RequestedAt,
            ResolvedAt = seed.Status == ReturnStatus.Requested ? null : seed.RequestedAt
        }).ToArray();
        foreach (var item in returnRequests) orderItem.ReturnRequests.Add(item);

        dbContext.AddRange(customer, sellerUser, category, store, product, order, payment);
        dbContext.ReturnRequests.AddRange(returnRequests);
        await dbContext.SaveChangesAsync();
        return new SeededReturns(store.Id, orderItem.Id, returnRequests.Select(item => item.Id).ToArray());
    }

    private async Task SeedPriorRefundsAsync(SeededReturns seeded, IReadOnlyCollection<PriorRefund> refunds)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        foreach (var prior in refunds)
        {
            var returnRequest = new ReturnRequest
            {
                OrderItemId = seeded.OrderItemId,
                CustomerId = Guid.NewGuid(),
                Quantity = 1,
                Reason = "Prior approved refund",
                Status = ReturnStatus.Approved,
                RefundAmount = prior.Amount,
                RequestedAt = DateTimeOffset.UtcNow,
                ResolvedAt = DateTimeOffset.UtcNow
            };
            dbContext.ReturnRequests.Add(returnRequest);
            dbContext.RefundRecords.Add(new RefundRecord
            {
                ReturnRequest = returnRequest,
                Status = prior.Status,
                Amount = prior.Amount
            });
        }
        await dbContext.SaveChangesAsync();
    }

    private static ApplicationUser TestUser(string email, string displayName) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        DisplayName = displayName,
        Status = AccountStatus.Active
    };

    private sealed record ReturnSeed(int Quantity, decimal Amount, ReturnStatus Status, DateTimeOffset RequestedAt);
    private sealed record PriorRefund(decimal Amount, RefundStatus Status);
    private sealed record SeededReturns(Guid StoreId, Guid OrderItemId, IReadOnlyList<Guid> ReturnRequestIds);
}
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

public sealed class AdminReportsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int nextSeedDay;

    [Fact]
    public async Task SalesReport_UsesPaidTimeInclusiveBoundsAndDistinctApprovedReturnRate()
    {
        var seed = await SeedReportsAsync();
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);
        var salesResponse = await client.GetAsync($"/api/admin/reports/sales?from={Encode(seed.From)}&to={Encode(seed.To)}");
        var salesContent = await salesResponse.Content.ReadAsStringAsync();
        Assert.True(salesResponse.IsSuccessStatusCode, salesContent);
        using var salesDocument = JsonDocument.Parse(salesContent);
        var response = salesDocument.RootElement;

        Assert.Equal(3, response.GetProperty("orderCount").GetInt32());
        Assert.Equal(375m, response.GetProperty("totalSales").GetDecimal());
        Assert.Equal(3, response.GetProperty("refundCount").GetInt32());
        Assert.Equal(60m, response.GetProperty("refundValue").GetDecimal());
        Assert.Equal(2, response.GetProperty("paidOrdersWithApprovedReturn").GetInt32());
        Assert.Equal(2m / 3m, response.GetProperty("refundRate").GetDecimal());
        Assert.True(response.GetProperty("refundRate").GetDecimal() <= 1m);
        Assert.Equal("ZAR", response.GetProperty("currency").GetString());

        var storeReport = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/sales?from={Encode(seed.From)}&to={Encode(seed.To)}&storeId={seed.FirstStoreId}");
        Assert.Equal(2, storeReport.GetProperty("orderCount").GetInt32());
        Assert.Equal(305m, storeReport.GetProperty("totalSales").GetDecimal());
        Assert.Equal(1m, storeReport.GetProperty("refundRate").GetDecimal());

        var deliveredReport = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/sales?from={Encode(seed.From)}&to={Encode(seed.To)}&storeId={seed.FirstStoreId}&status=Delivered");
        Assert.Equal(1, deliveredReport.GetProperty("orderCount").GetInt32());
        Assert.Equal(95m, deliveredReport.GetProperty("totalSales").GetDecimal());
        Assert.Equal(2, deliveredReport.GetProperty("refundCount").GetInt32());
        Assert.Equal(30m, deliveredReport.GetProperty("refundValue").GetDecimal());
        Assert.Equal(1m, deliveredReport.GetProperty("refundRate").GetDecimal());
    }

    [Fact]
    public async Task SalesReport_ZeroDataHasZeroRateAndMixedCurrenciesAreRejected()
    {
        var seed = await SeedReportsAsync();
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.SystemAdministrator);
        var emptyFrom = seed.To.AddYears(2);
        var empty = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/sales?from={Encode(emptyFrom)}&to={Encode(emptyFrom.AddDays(1))}");
        Assert.Equal(0, empty.GetProperty("orderCount").GetInt32());
        Assert.Equal(0m, empty.GetProperty("totalSales").GetDecimal());
        Assert.Equal(0m, empty.GetProperty("refundRate").GetDecimal());

        var currencyDate = seed.From.AddYears(10);
        await SeedCurrencyOrderAsync("USD", currencyDate);
        await SeedCurrencyOrderAsync("ZAR", currencyDate);
        var mixedCurrency = await client.GetAsync(
            $"/api/admin/reports/sales?from={Encode(currencyDate)}&to={Encode(currencyDate.AddDays(1))}");
        Assert.Equal(HttpStatusCode.BadRequest, mixedCurrency.StatusCode);
        using var error = JsonDocument.Parse(await mixedCurrency.Content.ReadAsStringAsync());
        Assert.True(error.RootElement.GetProperty("errors").TryGetProperty("Currency", out _));
    }

    [Fact]
    public async Task SalesReport_StoreFilterUsesAllocatedSellerOrderDeliveryOnce()
    {
        var seed = await SeedMultiStoreOrderAsync();
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);
        var range = $"from={Encode(seed.PaidAt)}&to={Encode(seed.PaidAt)}";

        var allStores = await client.GetFromJsonAsync<JsonElement>($"/api/admin/reports/sales?{range}");
        var firstStore = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/sales?{range}&storeId={seed.FirstStoreId}");
        var secondStore = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/sales?{range}&storeId={seed.SecondStoreId}");

        Assert.Equal(112m, allStores.GetProperty("totalSales").GetDecimal());
        Assert.Equal(1, allStores.GetProperty("orderCount").GetInt32());
        Assert.Equal(55m, firstStore.GetProperty("totalSales").GetDecimal());
        Assert.Equal(57m, secondStore.GetProperty("totalSales").GetDecimal());
        Assert.Equal(1, firstStore.GetProperty("orderCount").GetInt32());
        Assert.Equal(1, secondStore.GetProperty("orderCount").GetInt32());
        Assert.Equal(allStores.GetProperty("totalSales").GetDecimal(),
            firstStore.GetProperty("totalSales").GetDecimal() + secondStore.GetProperty("totalSales").GetDecimal());
    }

    [Fact]
    public async Task StockReport_IsCurrentPagedSnapshotAndFiltersStoreAndProductStatus()
    {
        var seed = await SeedReportsAsync();
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);
        var firstPage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/stock?storeId={seed.FirstStoreId}&status=Active&page=1&pageSize=1");
        Assert.Equal(2, firstPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("totalPages").GetInt32());
        var firstProduct = firstPage.GetProperty("items")[0];
        Assert.Equal(3, firstProduct.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(firstProduct.GetProperty("onHandQuantity").GetInt32()
            - firstProduct.GetProperty("reservedQuantity").GetInt32(),
            firstProduct.GetProperty("availableQuantity").GetInt32());
        Assert.True(firstProduct.GetProperty("availableQuantity").GetInt32()
            <= firstProduct.GetProperty("reorderLevel").GetInt32());

        var secondPage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/stock?storeId={seed.FirstStoreId}&status=Active&page=2&pageSize=1");
        Assert.NotEqual(firstProduct.GetProperty("productId").GetGuid(),
            secondPage.GetProperty("items")[0].GetProperty("productId").GetGuid());

        var draft = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/stock?storeId={seed.FirstStoreId}&status=Draft&page=1&pageSize=100");
        Assert.Equal(1, draft.GetProperty("totalCount").GetInt32());
        Assert.Equal("Draft", draft.GetProperty("items")[0].GetProperty("status").GetString());

        var invalid = await client.GetAsync("/api/admin/reports/stock?status=active&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task ServicePerformance_UsesInclusiveDeliveryTimeAndDatabaseAggregate()
    {
        var seed = await SeedReportsAsync();
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);
        var responseMessage = await client.GetAsync(
            $"/api/admin/reports/service-performance?from={Encode(seed.From)}&to={Encode(seed.To)}&storeId={seed.FirstStoreId}&status=Delivered");
        var responseContent = await responseMessage.Content.ReadAsStringAsync();
        Assert.True(responseMessage.IsSuccessStatusCode, responseContent);
        using var responseDocument = JsonDocument.Parse(responseContent);
        var response = responseDocument.RootElement;
        Assert.Equal(2, response.GetProperty("deliveredShipmentCount").GetInt32());
        Assert.Equal(4d, response.GetProperty("averageFulfilmentTurnaroundHours").GetDouble());

        var noRowsFrom = seed.To.AddYears(2);
        var noRows = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/reports/service-performance?from={Encode(noRowsFrom)}&to={Encode(noRowsFrom.AddDays(1))}");
        Assert.Equal(0, noRows.GetProperty("deliveredShipmentCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, noRows.GetProperty("averageFulfilmentTurnaroundHours").ValueKind);
    }

    [Fact]
    public void ServicePerformanceAverage_IsTranslatedByNpgsqlWithoutOpeningAConnection()
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused")
            .Options;
        using var dbContext = new MarketplaceDbContext(options);
        var query = dbContext.Shipments.AsNoTracking()
            .Where(shipment => shipment.Status == ShipmentStatus.Delivered
                && shipment.DispatchedAt.HasValue
                && shipment.DeliveredAt.HasValue
                && shipment.DeliveredAt >= shipment.DispatchedAt)
            .Select(shipment =>
                (shipment.DeliveredAt!.Value - shipment.DispatchedAt!.Value).TotalHours)
            .GroupBy(_ => 1)
            .Select(group => group.Average());

        var sql = query.ToQueryString();

        Assert.Contains("AVG", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeliveredAt", sql, StringComparison.Ordinal);
        Assert.Contains("DispatchedAt", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_ValidateUtcRangesMaximumRangeAndExactStatusNames()
    {
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.BusinessManager);
        var from = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var reversed = await client.GetAsync(
            $"/api/admin/reports/sales?from={Encode(to)}&to={Encode(from)}");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

        var tooLongTo = from.AddDays(367);
        var tooLong = await client.GetAsync(
            $"/api/admin/reports/service-performance?from={Encode(from)}&to={Encode(tooLongTo)}");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var nonUtc = from.ToOffset(TimeSpan.FromHours(2));
        var localOffset = await client.GetAsync(
            $"/api/admin/reports/sales?from={Encode(nonUtc)}&to={Encode(to)}");
        Assert.Equal(HttpStatusCode.BadRequest, localOffset.StatusCode);

        var badSalesStatus = await client.GetAsync("/api/admin/reports/sales?status=delivered");
        Assert.Equal(HttpStatusCode.BadRequest, badSalesStatus.StatusCode);
        var badStockStatus = await client.GetAsync("/api/admin/reports/stock?status=active");
        Assert.Equal(HttpStatusCode.BadRequest, badStockStatus.StatusCode);
        var badServiceStatus = await client.GetAsync("/api/admin/reports/service-performance?status=delivered");
        Assert.Equal(HttpStatusCode.BadRequest, badServiceStatus.StatusCode);
    }

    [Theory]
    [InlineData(AppRoles.BusinessManager)]
    [InlineData(AppRoles.SystemAdministrator)]
    public async Task ReportRoles_CanReadAllReports(string role)
    {
        using var client = await CreateAuthenticatedClientWithRoleAsync(role);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/api/admin/reports/sales?status=Cancelled")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/reports/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/api/admin/reports/service-performance")).StatusCode);
    }

    [Theory]
    [InlineData(AppRoles.Customer)]
    [InlineData(AppRoles.Seller)]
    [InlineData(AppRoles.ProductAdministrator)]
    [InlineData(AppRoles.FulfilmentEmployee)]
    public async Task OtherRoles_CannotReadReports(string role)
    {
        using var client = await CreateAuthenticatedClientWithRoleAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/reports/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/reports/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/reports/service-performance")).StatusCode);
    }

    [Fact]
    public async Task AnonymousUsers_CannotReadReports()
    {
        using var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/reports/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/reports/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/reports/service-performance")).StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientWithRoleAsync(string role)
    {
        var client = factory.CreateApiClient();
        var email = $"reports-role-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Reports",
            lastName = "Reader"
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
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        }
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", loginBody.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<ReportSeed> SeedReportsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var from = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)
            .AddDays(3 * Interlocked.Increment(ref nextSeedDay));
        var to = from.AddDays(1);
        var customer = TestUser($"report-customer-{suffix}@example.test", "Report Customer");
        var firstSellerUser = TestUser($"report-seller-a-{suffix}@example.test", "Report Seller A");
        var secondSellerUser = TestUser($"report-seller-b-{suffix}@example.test", "Report Seller B");
        var firstSeller = CreateSeller(firstSellerUser, suffix, "a");
        var secondSeller = CreateSeller(secondSellerUser, suffix, "b");
        var firstStore = CreateStore(firstSeller, suffix, "a");
        var secondStore = CreateStore(secondSeller, suffix, "b");
        var category = new Category { Name = $"Report category {suffix}", Slug = $"report-category-{suffix}" };

        var firstLowStock = CreateProduct(firstStore, category, suffix, "first-low", 5, 2, 3, ProductStatus.Active);
        var secondLowStock = CreateProduct(firstStore, category, suffix, "second-low", 2, 0, 2, ProductStatus.Active);
        var draftLowStock = CreateProduct(firstStore, category, suffix, "draft-low", 1, 0, 1, ProductStatus.Draft);
        _ = CreateProduct(firstStore, category, suffix, "healthy", 9, 0, 2, ProductStatus.Active);
        _ = CreateProduct(secondStore, category, suffix, "other-store-low", 1, 0, 2, ProductStatus.Active);

        var firstOrder = CreateOrder(customer, firstStore, firstSeller, firstLowStock, suffix, "sale-a",
            from, OrderStatus.Delivered, 100m, 10m, 5m, 2, 90m,
            [new ReturnSeed(ReturnStatus.Approved, 20m, to), new ReturnSeed(ReturnStatus.Received, 10m, from),
             new ReturnSeed(ReturnStatus.Rejected, 80m, from)],
            from.AddHours(-3), from);
        var secondOrder = CreateOrder(customer, firstStore, firstSeller, secondLowStock, suffix, "sale-b",
            to, OrderStatus.PartiallyRefunded, 200m, 0m, 10m, 2, 200m,
            [new ReturnSeed(ReturnStatus.Refunded, 30m, to)],
            to.AddHours(-5), to);
        var otherStoreOrder = CreateOrder(customer, secondStore, secondSeller, draftLowStock, suffix, "sale-c",
            from.AddHours(12), OrderStatus.Shipped, 70m, 0m, 0m, 1, 70m,
            [], to.AddDays(1), to.AddDays(1).AddHours(2));

        dbContext.AddRange(customer, firstSellerUser, secondSellerUser, category,
            firstStore, secondStore, firstLowStock, secondLowStock, draftLowStock,
            firstOrder.Order, firstOrder.SellerOrder, firstOrder.OrderItem,
            secondOrder.Order, secondOrder.SellerOrder, secondOrder.OrderItem,
            otherStoreOrder.Order, otherStoreOrder.SellerOrder, otherStoreOrder.OrderItem);
        dbContext.ReturnRequests.AddRange(firstOrder.Returns.Concat(secondOrder.Returns));
        dbContext.Shipments.AddRange(firstOrder.Shipment, secondOrder.Shipment, otherStoreOrder.Shipment);
        await dbContext.SaveChangesAsync();
        return new ReportSeed(from, to, firstStore.Id, secondStore.Id,
            firstLowStock.Id, secondLowStock.Id, draftLowStock.Id);
    }

    private async Task SeedCurrencyOrderAsync(string currency, DateTimeOffset paidAt)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var customer = TestUser($"currency-customer-{suffix}@example.test", "Currency Customer");
        var sellerUser = TestUser($"currency-seller-{suffix}@example.test", "Currency Seller");
        var seller = CreateSeller(sellerUser, suffix, "currency");
        var store = CreateStore(seller, suffix, "currency");
        var category = new Category { Name = $"Currency category {suffix}", Slug = $"currency-category-{suffix}" };
        var product = CreateProduct(store, category, suffix, "currency-product", 10, 0, 1, ProductStatus.Active);
        var order = CreateOrder(customer, store, seller, product, suffix, "currency", paidAt,
            OrderStatus.Paid, 10m, 0m, 0m, 1, 10m, [], null, null);
        order.Order.Currency = currency;
        dbContext.AddRange(customer, sellerUser, category, store, product,
            order.Order, order.SellerOrder, order.OrderItem);
        await dbContext.SaveChangesAsync();
    }

    private async Task<MultiStoreOrderSeed> SeedMultiStoreOrderAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var paidAt = DateTimeOffset.UtcNow;
        var customer = TestUser($"multi-store-customer-{suffix}@example.test", "Multi-store Customer");
        var firstSellerUser = TestUser($"multi-store-seller-a-{suffix}@example.test", "Multi-store Seller A");
        var secondSellerUser = TestUser($"multi-store-seller-b-{suffix}@example.test", "Multi-store Seller B");
        var firstSeller = CreateSeller(firstSellerUser, suffix, "multi-a");
        var secondSeller = CreateSeller(secondSellerUser, suffix, "multi-b");
        var firstStore = CreateStore(firstSeller, suffix, "multi-a");
        var secondStore = CreateStore(secondSeller, suffix, "multi-b");
        var category = new Category { Name = $"Multi-store category {suffix}", Slug = $"multi-store-category-{suffix}" };
        var firstProduct = CreateProduct(firstStore, category, suffix, "multi-a", 5, 0, 1, ProductStatus.Active);
        var secondProduct = CreateProduct(secondStore, category, suffix, "multi-b", 5, 0, 1, ProductStatus.Active);
        var order = new Order
        {
            OrderNumber = $"RPT-MULTI-{suffix[..16]}",
            Customer = customer,
            Status = OrderStatus.Paid,
            Subtotal = 100m,
            DeliveryTotal = 12m,
            GrandTotal = 112m,
            Currency = "ZAR",
            PaidAt = paidAt,
            PlacedAt = paidAt.AddMinutes(-5),
            ShippingAddress = new OrderAddress
            {
                RecipientName = customer.DisplayName,
                Line1 = "1 Multi-store Test Road",
                City = "Johannesburg",
                Province = "Gauteng",
                PostalCode = "2000",
                CountryCode = "ZA"
            }
        };

        AddSellerOrder(order, firstSeller, firstStore, firstProduct, 5m);
        AddSellerOrder(order, secondSeller, secondStore, secondProduct, 7m);
        dbContext.AddRange(customer, firstSellerUser, secondSellerUser, category,
            firstStore, secondStore, firstProduct, secondProduct, order);
        await dbContext.SaveChangesAsync();
        return new MultiStoreOrderSeed(paidAt, firstStore.Id, secondStore.Id);
    }

    private static void AddSellerOrder(
        Order order,
        SellerProfile seller,
        Store store,
        Product product,
        decimal delivery)
    {
        var sellerOrder = new SellerOrder
        {
            Order = order,
            Seller = seller,
            Store = store,
            Status = SellerOrderStatus.Delivered,
            Subtotal = 50m,
            DeliveryTotal = delivery,
            SellerNetAmount = 50m
        };
        sellerOrder.Items.Add(new OrderItem
        {
            SellerOrder = sellerOrder,
            Product = product,
            SkuSnapshot = product.Sku,
            ProductNameSnapshot = product.Name,
            Quantity = 1,
            UnitPrice = 50m,
            LineTotal = 50m
        });
        order.SellerOrders.Add(sellerOrder);
    }

    private static ApplicationUser TestUser(string email, string name) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        DisplayName = name,
        Status = AccountStatus.Active
    };

    private static SellerProfile CreateSeller(ApplicationUser user, string suffix, string label) => new()
    {
        User = user,
        TradingName = $"Report Seller {label} {suffix}",
        Status = SellerStatus.Approved,
        CommissionRate = 0.1m
    };

    private static Store CreateStore(SellerProfile seller, string suffix, string label) => new()
    {
        Seller = seller,
        Name = $"Report Store {label} {suffix}",
        Slug = $"report-store-{label}-{suffix}",
        Status = StoreStatus.Active
    };

    private static Product CreateProduct(
        Store store,
        Category category,
        string suffix,
        string label,
        int onHand,
        int reserved,
        int reorder,
        ProductStatus status)
    {
        var product = new Product
        {
            Store = store,
            Sku = $"REPORT-{label}-{suffix}",
            Name = $"Report {label} {suffix}",
            Slug = $"report-{label}-{suffix}",
            Price = 50m,
            Status = status,
            Inventory = new InventoryItem { OnHandQuantity = onHand, ReservedQuantity = reserved, ReorderLevel = reorder }
        };
        product.Inventory.Product = product;
        product.Categories.Add(new ProductCategory { Product = product, Category = category });
        return product;
    }

    private static SeededOrder CreateOrder(
        ApplicationUser customer,
        Store store,
        SellerProfile seller,
        Product product,
        string suffix,
        string label,
        DateTimeOffset paidAt,
        OrderStatus status,
        decimal subtotal,
        decimal discount,
        decimal delivery,
        int quantity,
        decimal lineTotal,
        IReadOnlyCollection<ReturnSeed> returns,
        DateTimeOffset? dispatchedAt,
        DateTimeOffset? deliveredAt)
    {
        var order = new Order
        {
            OrderNumber = $"RPT-{label}-{suffix[..16]}",
            Customer = customer,
            Status = status,
            Subtotal = subtotal,
            DiscountTotal = discount,
            DeliveryTotal = delivery,
            GrandTotal = subtotal - discount + delivery,
            Currency = "ZAR",
            PaidAt = paidAt,
            PlacedAt = paidAt.AddHours(-1),
            ShippingAddress = new OrderAddress
            {
                RecipientName = customer.DisplayName,
                Line1 = "1 Report Test Road",
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
            Status = status == OrderStatus.Shipped ? SellerOrderStatus.Shipped : SellerOrderStatus.Delivered,
            Subtotal = subtotal,
            DiscountTotal = discount,
            DeliveryTotal = delivery,
            SellerNetAmount = subtotal - discount
        };
        var orderItem = new OrderItem
        {
            SellerOrder = sellerOrder,
            Product = product,
            SkuSnapshot = product.Sku,
            ProductNameSnapshot = product.Name,
            Quantity = quantity,
            UnitPrice = product.Price,
            DiscountAmount = discount,
            LineTotal = lineTotal
        };
        var returnRequests = returns.Select(seed => new ReturnRequest
        {
            OrderItem = orderItem,
            Customer = customer,
            Quantity = 1,
            Reason = "Report test return",
            Status = seed.Status,
            RefundAmount = seed.Amount,
            RequestedAt = seed.RequestedAt,
            ResolvedAt = seed.Status == ReturnStatus.Requested ? null : seed.RequestedAt
        }).ToArray();
        foreach (var returnRequest in returnRequests) orderItem.ReturnRequests.Add(returnRequest);
        order.SellerOrders.Add(sellerOrder);
        sellerOrder.Items.Add(orderItem);
        var shipment = new Shipment
        {
            SellerOrder = sellerOrder,
            Status = ShipmentStatus.Delivered,
            DispatchedAt = dispatchedAt,
            DeliveredAt = deliveredAt
        };
        sellerOrder.Shipments.Add(shipment);
        return new SeededOrder(order, sellerOrder, orderItem, returnRequests, shipment);
    }

    private static string Encode(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    private sealed record ReturnSeed(ReturnStatus Status, decimal Amount, DateTimeOffset RequestedAt);
    private sealed record SeededOrder(
        Order Order,
        SellerOrder SellerOrder,
        OrderItem OrderItem,
        IReadOnlyCollection<ReturnRequest> Returns,
        Shipment Shipment);
    private sealed record ReportSeed(
        DateTimeOffset From,
        DateTimeOffset To,
        Guid FirstStoreId,
        Guid SecondStoreId,
        Guid FirstLowStockProductId,
        Guid SecondLowStockProductId,
        Guid DraftLowStockProductId);
    private sealed record MultiStoreOrderSeed(DateTimeOffset PaidAt, Guid FirstStoreId, Guid SecondStoreId);
}
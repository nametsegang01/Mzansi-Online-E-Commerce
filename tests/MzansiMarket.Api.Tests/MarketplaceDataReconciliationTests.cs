using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class MarketplaceDataReconciliationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Reconciliation_ActivatesOnlyApprovedSellerDrafts()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var approved = ProductFor($"approved-{suffix}", SellerStatus.Approved, StoreStatus.Active);
        var pending = ProductFor($"pending-{suffix}", SellerStatus.Pending, StoreStatus.Draft);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            db.Products.AddRange(approved, pending);
            await db.SaveChangesAsync();
        }

        await MarketplaceDataReconciliation.ActivateApprovedSellerDraftsAsync(
            factory.Services, NullLogger.Instance);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        Assert.Equal(ProductStatus.Active, (await verification.Products.SingleAsync(item => item.Id == approved.Id)).Status);
        Assert.Equal(ProductStatus.Draft, (await verification.Products.SingleAsync(item => item.Id == pending.Id)).Status);
    }

    private static Product ProductFor(string suffix, SellerStatus sellerStatus, StoreStatus storeStatus)
    {
        var seller = new SellerProfile { UserId = Guid.NewGuid(), TradingName = suffix, Status = sellerStatus };
        var store = new Store { Name = suffix, Slug = suffix, Status = storeStatus, Seller = seller, SellerId = seller.UserId };
        seller.Store = store;
        var product = new Product { Store = store, StoreId = store.Id, Sku = suffix.ToUpperInvariant(), Name = suffix, Slug = suffix, Price = 1, Status = ProductStatus.Draft };
        product.Inventory = new InventoryItem { Product = product, ProductId = product.Id, OnHandQuantity = 1 };
        return product;
    }
}

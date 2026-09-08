using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Data;

public static class MarketplaceDataReconciliation
{
    public static async Task ActivateApprovedSellerDraftsAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var eligible = db.Products.Where(product =>
            !product.IsDeleted
            && product.Status == ProductStatus.Draft
            && product.Store.Status == StoreStatus.Active
            && product.Store.Seller.Status == SellerStatus.Approved);
        var now = DateTimeOffset.UtcNow;
        int activated;
        if (db.Database.IsRelational())
        {
            activated = await eligible.ExecuteUpdateAsync(setters => setters
                .SetProperty(product => product.Status, ProductStatus.Active)
                .SetProperty(product => product.UpdatedAt, now), cancellationToken);
        }
        else
        {
            var products = await eligible.ToArrayAsync(cancellationToken);
            foreach (var product in products)
            {
                product.Status = ProductStatus.Active;
                product.UpdatedAt = now;
            }
            activated = products.Length;
            if (activated > 0) await db.SaveChangesAsync(cancellationToken);
        }

        if (activated > 0) logger.LogInformation(
            "Activated {ProductCount} existing draft products for approved sellers.", activated);
    }
}

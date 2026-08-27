using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MzansiMarket.Api.Data;

public sealed class MarketplaceDbContextFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args)
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=mzansi_market_design;Username=postgres";

        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseNpgsql(PostgresConnectionString.Normalize(configured), postgres =>
                postgres.SetPostgresVersion(18, 0))
            .Options;

        return new MarketplaceDbContext(options);
    }
}

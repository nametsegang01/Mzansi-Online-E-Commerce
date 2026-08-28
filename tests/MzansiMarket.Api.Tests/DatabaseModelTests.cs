using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;
using Npgsql;

namespace MzansiMarket.Api.Tests;

public sealed class DatabaseModelTests
{
    [Fact]
    public void RenderUrl_IsConvertedToNpgsqlConnectionString()
    {
        var normalized = PostgresConnectionString.Normalize(
            "postgresql://seller%40mzansi:p%40ss%3Aword@db.example.com:5433/mzansi_market?sslmode=require");
        var parsed = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("seller@mzansi", parsed.Username);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("mzansi_market", parsed.Database);
        Assert.Equal(SslMode.Require, parsed.SslMode);
        Assert.False(parsed.IncludeErrorDetail);
    }

    [Fact]
    public void KeyValueConnectionString_IsPreserved()
    {
        const string value = "Host=localhost;Database=mzansi;Username=postgres;Password=test-only";

        Assert.Equal(value, PostgresConnectionString.Normalize(value));
    }

    [Fact]
    public void Model_IncludesSellerCommerceAndAuditBoundaries()
    {
        using var db = CreateContext();
        var model = db.Model;

        Assert.Equal("SellerProfiles", model.FindEntityType(typeof(SellerProfile))!.GetTableName());
        Assert.Equal("Stores", model.FindEntityType(typeof(Store))!.GetTableName());
        Assert.Equal("SellerOrders", model.FindEntityType(typeof(SellerOrder))!.GetTableName());
        Assert.Equal("SellerPayouts", model.FindEntityType(typeof(SellerPayout))!.GetTableName());
        Assert.Equal("audit", model.FindEntityType(typeof(AuditEntry))!.GetSchema());
        Assert.Equal("identity", model.FindEntityType(typeof(DataProtectionKey))!.GetSchema());
        Assert.True(model.GetEntityTypes().Count() >= 30);
    }

    [Fact]
    public void Model_DoesNotPersistRawCardData()
    {
        using var db = CreateContext();
        var forbiddenNames = new[] { "CardNumber", "Pan", "Cvv", "Cvc", "SecurityCode" };

        var persistedPropertyNames = db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(forbiddenNames, persistedPropertyNames.Contains);
    }

    [Fact]
    public void GeneratedSchema_ContainsCriticalDatabaseConstraints()
    {
        using var db = CreateContext();
        var sql = db.Database.GenerateCreateScript();

        Assert.Contains("CK_InventoryItems_Quantities", sql);
        Assert.Contains("CK_CartItems_Quantity", sql);
        Assert.Contains("CK_Orders_Totals", sql);
        Assert.Contains("CREATE SCHEMA identity", sql);
        Assert.Contains("CREATE SCHEMA audit", sql);
        Assert.Contains("DataProtectionKeys", sql);
    }

    private static MarketplaceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseNpgsql("Host=localhost;Database=model_tests;Username=postgres;Password=test-only")
            .Options;

        return new MarketplaceDbContext(options);
    }
}

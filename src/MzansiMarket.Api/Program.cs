using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

var builder = WebApplication.CreateBuilder(args);

var configuredConnectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["DATABASE_URL"];

if (string.IsNullOrWhiteSpace(configuredConnectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings__DefaultConnection or DATABASE_URL. " +
        "Database credentials must not be committed to source control.");
}

var connectionString = PostgresConnectionString.Normalize(configuredConnectionString);

builder.Services.AddDbContextPool<MarketplaceDbContext>(options =>
    options.UseNpgsql(connectionString, postgres =>
    {
        postgres.SetPostgresVersion(18, 0);
        postgres.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
    }));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<MarketplaceDbContext>();

builder.Services.AddHealthChecks().AddDbContextCheck<MarketplaceDbContext>("postgres");

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "Mzansi Market API",
    database = "PostgreSQL",
    environment = app.Environment.EnvironmentName
}));
app.MapHealthChecks("/health/database");

app.Run();

public partial class Program;

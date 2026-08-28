using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Authorization;

namespace MzansiMarket.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"mzansi-api-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Database=unused;Username=postgres;Password=test-only",
                ["RateLimiting:AuthenticationPermitLimit"] = "100",
                ["SandboxPayments:WebhookSecret"] = "test-webhook-secret-only"
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MarketplaceDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MarketplaceDbContext>>();
            services.RemoveAll<MarketplaceDbContext>();
            services.AddDbContext<MarketplaceDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Database.EnsureCreated();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var roleName in AppRoles.All)
        {
            if (!roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
            {
                var result = roleManager.CreateAsync(new IdentityRole<Guid>(roleName)).GetAwaiter().GetResult();
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Unable to seed test role {roleName}.");
                }
            }
        }
        return host;
    }

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });
}

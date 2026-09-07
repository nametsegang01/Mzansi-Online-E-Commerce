using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class AdministratorBootstrapTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Bootstrap_CreatesAdministratorOnceWithoutResettingTheAccount()
    {
        var email = $"bootstrap-{Guid.NewGuid():N}@example.test";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Email"] = email,
            ["BootstrapAdmin:Password"] = "BootstrapOnly!2345",
            ["BootstrapAdmin:DisplayName"] = "Marketplace Administrator"
        }).Build();

        await AdministratorBootstrap.EnsureAsync(factory.Services, configuration, NullLogger.Instance);
        await AdministratorBootstrap.EnsureAsync(factory.Services, configuration, NullLogger.Instance);

        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Equal("Marketplace Administrator", user.DisplayName);
        Assert.True(await manager.IsInRoleAsync(user, AppRoles.SystemAdministrator));
    }
}

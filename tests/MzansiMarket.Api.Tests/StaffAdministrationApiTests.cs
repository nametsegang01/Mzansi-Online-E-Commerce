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

public sealed class StaffAdministrationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SystemAdministrator_ProvisioningHashesPasswordAndAuditsWithoutSensitiveData()
    {
        using var client = await CreateAuthenticatedCustomerWithRoleAsync(AppRoles.SystemAdministrator);
        var email = $"staff-{Guid.NewGuid():N}@example.test";
        const string initialPassword = "TemporaryStaff!2345";
        var response = await client.PostAsJsonAsync("/api/admin/staff", new
        {
            email,
            displayName = "Marketplace Manager",
            role = AppRoles.BusinessManager,
            password = initialPassword
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(initialPassword, responseContent, StringComparison.Ordinal);
        using var responseBody = JsonDocument.Parse(responseContent);
        Assert.Equal(email, responseBody.RootElement.GetProperty("email").GetString());
        Assert.Equal(AppRoles.BusinessManager,
            Assert.Single(responseBody.RootElement.GetProperty("roles").EnumerateArray())!.GetString());
        Assert.False(responseBody.RootElement.TryGetProperty("password", out _));

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.NotEqual(initialPassword, user.PasswordHash);
        Assert.True(await userManager.CheckPasswordAsync(user, initialPassword));

        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var audit = await dbContext.AuditEntries.SingleAsync(entry =>
            entry.EntityId == user.Id.ToString() && entry.Action == "StaffAccountProvisioned");
        Assert.NotNull(audit.ChangesJson);
        Assert.DoesNotContain(initialPassword, audit.ChangesJson, StringComparison.Ordinal);
        using var changes = JsonDocument.Parse(audit.ChangesJson);
        Assert.Equal(AppRoles.BusinessManager, changes.RootElement.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(AppRoles.Customer)]
    [InlineData(AppRoles.Seller)]
    [InlineData(AppRoles.ProductAdministrator)]
    [InlineData(AppRoles.FulfilmentEmployee)]
    [InlineData(AppRoles.BusinessManager)]
    public async Task NonSystemAdministrator_CannotProvisionStaff(string role)
    {
        using var client = await CreateAuthenticatedCustomerWithRoleAsync(role);
        var response = await client.PostAsJsonAsync("/api/admin/staff", new
        {
            email = $"denied-staff-{Guid.NewGuid():N}@example.test",
            displayName = "Denied Staff Member",
            role = AppRoles.BusinessManager,
            password = "TemporaryStaff!2345"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Provisioning_UsesIdentityPasswordRulesAndRejectsSystemAdministratorRole()
    {
        using var client = await CreateAuthenticatedCustomerWithRoleAsync(AppRoles.SystemAdministrator);
        var weakPassword = await client.PostAsJsonAsync("/api/admin/staff", new
        {
            email = $"weak-password-{Guid.NewGuid():N}@example.test",
            displayName = "Weak Password User",
            role = AppRoles.BusinessManager,
            password = "lowercasepassword"
        });
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        using (var errors = JsonDocument.Parse(await weakPassword.Content.ReadAsStringAsync()))
        {
            Assert.True(errors.RootElement.GetProperty("errors").TryGetProperty("Password", out _));
        }

        var privilegedRole = await client.PostAsJsonAsync("/api/admin/staff", new
        {
            email = $"extra-admin-{Guid.NewGuid():N}@example.test",
            displayName = "Additional System Administrator",
            role = AppRoles.SystemAdministrator,
            password = "TemporaryStaff!2345"
        });
        Assert.Equal(HttpStatusCode.BadRequest, privilegedRole.StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedCustomerWithRoleAsync(string role)
    {
        var client = factory.CreateApiClient();
        var email = $"role-user-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Test",
            lastName = "Account"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        if (role != AppRoles.Customer)
        {
            using var scope = factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            var result = await userManager.AddToRoleAsync(user, role);
            Assert.True(result.Succeeded);
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
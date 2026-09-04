using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class AuthenticationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Customer_CanRegisterLoginRefreshInspectAndInvalidateSessions()
    {
        using var client = factory.CreateApiClient();
        var email = $"customer-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";

        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Naledi",
            lastName = "Dlamini",
            mobileNumber = "+27 71 000 0000"
        });

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registrationBody = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        Assert.Contains(AppRoles.Customer,
            registrationBody.RootElement.GetProperty("roles").EnumerateArray().Select(item => item.GetString()));

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = loginBody.RootElement.GetProperty("accessToken").GetString();
        var refreshToken = loginBody.RootElement.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var meBody = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(email, meBody.RootElement.GetProperty("email").GetString());
        Assert.Equal("Naledi", meBody.RootElement.GetProperty("customer").GetProperty("firstName").GetString());

        client.DefaultRequestHeaders.Authorization = null;
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var logoutAll = await client.PostAsJsonAsync("/api/auth/logout-all", new { });
        Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var invalidatedRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidatedRefresh.StatusCode);
    }

    [Fact]
    public async Task InvalidAndUnauthenticatedRequests_AreRejectedWithoutSensitiveDetails()
    {
        using var client = factory.CreateApiClient();

        var weakRegistration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email = "not-an-email",
            password = "weak",
            firstName = "",
            lastName = "Dlamini"
        });
        Assert.Equal(HttpStatusCode.BadRequest, weakRegistration.StatusCode);

        var unauthenticated = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var failedLogin = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"missing-{Guid.NewGuid():N}@example.test",
            password = "WrongPassword!123"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
        var body = await failedLogin.Content.ReadAsStringAsync();
        Assert.DoesNotContain("missing-", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepeatedInvalidPasswords_LockTheAccount()
    {
        using var client = factory.CreateApiClient();
        var email = $"lockout-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";

        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Thandi",
            lastName = "Khumalo"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email,
                password = "Incorrect!2345"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var lockedOut = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await userManager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task Cors_AllowsTheDeployedStorefrontAndRejectsUnknownOrigins()
    {
        using var client = factory.CreateApiClient();
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        allowed.Headers.Add("Origin", "https://mzansi-market-customer.onrender.com");
        allowed.Headers.Add("Access-Control-Request-Method", "POST");

        var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal(
            "https://mzansi-market-customer.onrender.com",
            Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Origin")));

        using var rejected = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        rejected.Headers.Add("Origin", "https://untrusted.example.test");
        rejected.Headers.Add("Access-Control-Request-Method", "POST");

        var rejectedResponse = await client.SendAsync(rejected);
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SellerRegistration_CreatesPendingSellerAndRequiresApprovalPolicy()
    {
        using var client = factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"seller-{suffix}@example.test";
        var storeAddress = $"Ubuntu Goods, Shop 12 / {suffix}";
        var expectedStoreSlug = $"ubuntu-goods-shop-12-{suffix}";

        var registration = await client.PostAsJsonAsync("/api/auth/register/seller", new
        {
            email,
            password = "LocalOnly!2345",
            firstName = "Lerato",
            lastName = "Mokoena",
            mobileNumber = "+27 72 000 0000",
            tradingName = "Ubuntu Goods",
            registrationNumber = "FICTIONAL-001",
            storeSlug = storeAddress,
            supportEmail = email
        });

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var body = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        Assert.Equal(SellerStatus.Pending.ToString(), body.RootElement.GetProperty("sellerStatus").GetString());

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Role, AppRoles.Customer),
            new Claim(ClaimTypes.Role, AppRoles.Seller)
        ], "Test", ClaimTypes.Name, ClaimTypes.Role));

        var pendingResult = await authorization.AuthorizeAsync(principal, null, AuthorizationPolicies.ApprovedSeller);
        Assert.False(pendingResult.Succeeded);
        var pendingCatalogueResult = await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.CatalogueManagement);
        Assert.False(pendingCatalogueResult.Succeeded);

        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var seller = await dbContext.SellerProfiles.FindAsync(user.Id);
        var store = Assert.Single(dbContext.Stores.Where(item => item.SellerId == user.Id));
        Assert.NotNull(seller);
        Assert.Equal(SellerStatus.Pending, seller.Status);
        Assert.Equal(StoreStatus.Draft, store.Status);
        Assert.Equal(expectedStoreSlug, store.Slug);

        seller.Status = SellerStatus.Approved;
        store.Status = StoreStatus.Active;
        await dbContext.SaveChangesAsync();
        var approvedResult = await authorization.AuthorizeAsync(principal, null, AuthorizationPolicies.ApprovedSeller);
        Assert.True(approvedResult.Succeeded);
        var approvedCatalogueResult = await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.CatalogueManagement);
        Assert.True(approvedCatalogueResult.Succeeded);

        user.Status = AccountStatus.Suspended;
        await userManager.UpdateAsync(user);
        var suspendedResult = await authorization.AuthorizeAsync(
            principal, null, AuthorizationPolicies.ApprovedSeller);
        Assert.False(suspendedResult.Succeeded);
    }
}

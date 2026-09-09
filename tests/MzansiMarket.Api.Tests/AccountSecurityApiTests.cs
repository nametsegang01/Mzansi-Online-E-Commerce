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

public sealed class AccountSecurityApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CustomerPassword_IsHashedAndCanBeChangedOnlyWithTheCurrentPassword()
    {
        using var client = factory.CreateApiClient();
        var email = $"secure-customer-{Guid.NewGuid():N}@example.test";
        const string currentPassword = "CurrentOnly!2345";
        const string newPassword = "NewSecureOnly!5678";
        await RegisterCustomerAsync(client, email, currentPassword);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user?.PasswordHash);
            Assert.NotEqual(currentPassword, user.PasswordHash);
            Assert.NotEqual(PasswordVerificationResult.Failed,
                userManager.PasswordHasher.VerifyHashedPassword(user!, user!.PasswordHash!, currentPassword));
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginAsync(client, email, currentPassword));
        var wrongCurrent = await client.PostAsJsonAsync("/api/account/change-password", new
        {
            currentPassword = "Incorrect!2345",
            newPassword
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var changed = await client.PostAsJsonAsync("/api/account/change-password", new { currentPassword, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = currentPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = newPassword })).StatusCode);
    }

    [Fact]
    public async Task SellerCanUpdateSharedProfileAndSecurelyChangeEmail()
    {
        using var client = factory.CreateApiClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"secure-seller-{suffix}@example.test";
        const string password = "SellerOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/seller", new
        {
            email,
            password,
            firstName = "Lerato",
            lastName = "Mokoena",
            mobileNumber = "+27 72 000 0000",
            tradingName = "Secure Studio",
            storeSlug = $"secure-studio-{suffix}"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginAsync(client, email, password));

        var update = await client.PutAsJsonAsync("/api/account/profile", new
        {
            displayName = "Lerato N. Mokoena",
            mobileNumber = "+27 82 111 2233"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updateBody = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal("Lerato N. Mokoena", updateBody.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("+27 82 111 2233", updateBody.RootElement.GetProperty("mobileNumber").GetString());

        var newEmail = $"renamed-seller-{suffix}@example.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/change-email", new
        {
            newEmail,
            currentPassword = "Incorrect!2345"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/account/change-email", new
        {
            newEmail,
            currentPassword = password
        })).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email = newEmail, password })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var seller = await dbContext.Users.SingleAsync(user => user.Email == newEmail);
        var customerProfile = await dbContext.CustomerProfiles.SingleAsync(profile => profile.UserId == seller.Id);
        Assert.Equal("+27 82 111 2233", seller.PhoneNumber);
        Assert.Equal(seller.PhoneNumber, customerProfile.MobileNumber);
        Assert.NotEqual(password, seller.PasswordHash);
    }

    [Fact]
    public async Task AdministratorCanManageProfileAndOwnedAddresses()
    {
        var email = $"account-admin-{Guid.NewGuid():N}@example.test";
        const string password = "AdminOnly!2345";
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true,
                DisplayName = "Account Administrator", Status = AccountStatus.Active
            };
            Assert.True((await userManager.CreateAsync(admin, password)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(admin, AppRoles.SystemAdministrator)).Succeeded);
            Assert.NotEqual(password, admin.PasswordHash);
        }

        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginAsync(client, email, password));
        var profile = await client.PutAsJsonAsync("/api/account/profile", new
        {
            displayName = "Primary Administrator",
            mobileNumber = "+27 83 222 3344"
        });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var address = await client.PostAsJsonAsync("/api/account/addresses", new
        {
            type = "Both",
            recipientName = "Primary Administrator",
            line1 = "1 Admin Avenue",
            city = "Pretoria",
            province = "Gauteng",
            postalCode = "0002",
            countryCode = "ZA",
            isDefault = true
        });
        Assert.Equal(HttpStatusCode.Created, address.StatusCode);
        Assert.Single(await client.GetFromJsonAsync<JsonElement[]>("/api/account/addresses") ?? []);
    }

    private static async Task RegisterCustomerAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email, password, firstName = "Secure", lastName = "Customer", mobileNumber = "+27 71 123 4567"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }
}

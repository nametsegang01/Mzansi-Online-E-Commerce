using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Tests;

public sealed class CategoryAdministrationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(AppRoles.ProductAdministrator)]
    [InlineData(AppRoles.SystemAdministrator)]
    public async Task AuthorizedStaff_CanCreateUpdateListAndDeactivateCategories(string role)
    {
        using var client = await CreateAuthenticatedClientWithRoleAsync(role);
        var slug = $"managed-{Guid.NewGuid():N}";
        var created = await client.PostAsJsonAsync("/api/admin/categories", CategoryBody("Managed category", slug));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var categoryId = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.True(createdBody.RootElement.GetProperty("isActive").GetBoolean());

        var updatedSlug = $"{slug}-updated";
        var updated = await client.PutAsJsonAsync($"/api/admin/categories/{categoryId}",
            CategoryBody("Updated category", updatedSlug));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal(updatedSlug, updatedBody.RootElement.GetProperty("slug").GetString());

        var deactivated = await client.PutAsJsonAsync($"/api/admin/categories/{categoryId}",
            CategoryBody("Updated category", updatedSlug, isActive: false));
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        using var deactivatedBody = JsonDocument.Parse(await deactivated.Content.ReadAsStringAsync());
        Assert.False(deactivatedBody.RootElement.GetProperty("isActive").GetBoolean());

        var adminList = await client.GetFromJsonAsync<JsonElement[]>("/api/admin/categories");
        Assert.Contains(adminList!, category =>
            category.GetProperty("id").GetGuid() == categoryId
            && !category.GetProperty("isActive").GetBoolean());
        using var publicClient = factory.CreateApiClient();
        var publicList = await publicClient.GetFromJsonAsync<JsonElement[]>("/api/categories");
        Assert.DoesNotContain(publicList!, category => category.GetProperty("id").GetGuid() == categoryId);
    }

    [Fact]
    public async Task CategoryValidation_RejectsDuplicateSlugInvalidParentSelfParentAndCycles()
    {
        using var client = await CreateAuthenticatedClientWithRoleAsync(AppRoles.ProductAdministrator);
        var rootSlug = $"root-{Guid.NewGuid():N}";
        var rootId = await CreateCategoryAsync(client, "Root category", rootSlug);
        using var duplicate = await client.PostAsJsonAsync("/api/admin/categories", CategoryBody("Duplicate", rootSlug));
        await AssertValidationFieldAsync(duplicate, "Slug");

        using var caseVariant = await client.PostAsJsonAsync("/api/admin/categories",
            CategoryBody("Case variant", rootSlug.ToUpperInvariant()));
        await AssertValidationFieldAsync(caseVariant, "Slug");

        using var invalidSlug = await client.PostAsJsonAsync("/api/admin/categories", CategoryBody("Invalid", "Bad_slug"));
        await AssertValidationFieldAsync(invalidSlug, "Slug");

        using var invalidParent = await client.PostAsJsonAsync("/api/admin/categories",
            CategoryBody("Missing parent", $"missing-parent-{Guid.NewGuid():N}", Guid.NewGuid()));
        await AssertValidationFieldAsync(invalidParent, "ParentCategoryId");

        var childId = await CreateCategoryAsync(client, "Child category",
            $"child-{Guid.NewGuid():N}", rootId);
        using var selfParent = await client.PutAsJsonAsync($"/api/admin/categories/{rootId}",
            CategoryBody("Root category", rootSlug, rootId));
        await AssertValidationFieldAsync(selfParent, "ParentCategoryId");

        using var cycle = await client.PutAsJsonAsync($"/api/admin/categories/{rootId}",
            CategoryBody("Root category", rootSlug, childId));
        await AssertValidationFieldAsync(cycle, "ParentCategoryId");
    }

    [Theory]
    [InlineData(AppRoles.Customer)]
    [InlineData(AppRoles.Seller)]
    [InlineData(AppRoles.FulfilmentEmployee)]
    [InlineData(AppRoles.BusinessManager)]
    public async Task OtherAuthenticatedRoles_CannotManageCategories(string role)
    {
        using var client = await CreateAuthenticatedClientWithRoleAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/admin/categories", CategoryBody("Denied", $"denied-{Guid.NewGuid():N}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/admin/categories/{Guid.NewGuid()}", CategoryBody("Denied", $"denied-{Guid.NewGuid():N}"))).StatusCode);
    }

    [Fact]
    public async Task AnonymousUsers_CannotManageCategories()
    {
        using var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/admin/categories", CategoryBody("Anonymous", $"anonymous-{Guid.NewGuid():N}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync($"/api/admin/categories/{Guid.NewGuid()}", CategoryBody("Anonymous", $"anonymous-{Guid.NewGuid():N}"))).StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientWithRoleAsync(string role)
    {
        var client = factory.CreateApiClient();
        var email = $"category-role-{Guid.NewGuid():N}@example.test";
        const string password = "LocalOnly!2345";
        var registration = await client.PostAsJsonAsync("/api/auth/register/customer", new
        {
            email,
            password,
            firstName = "Category",
            lastName = "Tester"
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<Guid> CreateCategoryAsync(
        HttpClient client,
        string name,
        string slug,
        Guid? parentCategoryId = null)
    {
        var response = await client.PostAsJsonAsync("/api/admin/categories", CategoryBody(name, slug, parentCategoryId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static object CategoryBody(
        string name,
        string slug,
        Guid? parentCategoryId = null,
        bool isActive = true) => new
    {
        name,
        slug,
        parentCategoryId,
        isActive
    };

    private static async Task AssertValidationFieldAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }
}
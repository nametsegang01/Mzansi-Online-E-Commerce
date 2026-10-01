using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;
using MzansiMarket.Api.Services;

namespace MzansiMarket.Api.Endpoints;

public static class CategoryAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapCategoryAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/categories")
            .WithTags("Category administration")
            .RequireAuthorization(AuthorizationPolicies.CategoryAdministration);

        group.MapGet("/", GetCategoriesAsync)
            .Produces<IReadOnlyCollection<AdminCategoryResponse>>();
        group.MapPost("/", CreateCategoryAsync)
            .Produces<AdminCategoryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        group.MapPut("/{categoryId:guid}", UpdateCategoryAsync)
            .Produces<AdminCategoryResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetCategoriesAsync(
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var categories = await ProjectCategories(dbContext)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(categories);
    }

    private static async Task<IResult> CreateCategoryAsync(
        CategoryAdministrationRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        MarketplaceChangeFeed changes,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(request, null, dbContext, cancellationToken);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            ParentCategoryId = request.ParentCategoryId,
            IsActive = request.IsActive
        };
        dbContext.Categories.Add(category);
        AddAudit(dbContext, principal, category, "CategoryCreated", httpContext,
            new { category.Name, category.Slug, category.ParentCategoryId, category.IsActive });
        await dbContext.SaveChangesAsync(cancellationToken);
        changes.Publish("catalogue");

        var response = await ProjectCategories(dbContext)
            .SingleAsync(item => item.Id == category.Id, cancellationToken);
        return Results.Created($"/api/admin/categories/{category.Id}", response);
    }

    private static async Task<IResult> UpdateCategoryAsync(
        Guid categoryId,
        CategoryAdministrationRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        MarketplaceChangeFeed changes,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .SingleOrDefaultAsync(item => item.Id == categoryId, cancellationToken);
        if (category is null) return Results.NotFound();

        var errors = await ValidateAsync(request, categoryId, dbContext, cancellationToken);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var previous = new
        {
            category.Name,
            category.Slug,
            category.ParentCategoryId,
            category.IsActive
        };
        category.Name = request.Name.Trim();
        category.Slug = request.Slug.Trim().ToLowerInvariant();
        category.ParentCategoryId = request.ParentCategoryId;
        category.IsActive = request.IsActive;
        var action = previous.IsActive && !category.IsActive ? "CategoryDeactivated" : "CategoryUpdated";
        AddAudit(dbContext, principal, category, action, httpContext,
            new { previous, current = new { category.Name, category.Slug, category.ParentCategoryId, category.IsActive } });
        await dbContext.SaveChangesAsync(cancellationToken);
        changes.Publish("catalogue");

        var response = await ProjectCategories(dbContext)
            .SingleAsync(item => item.Id == category.Id, cancellationToken);
        return Results.Ok(response);
    }

    private static IQueryable<AdminCategoryResponse> ProjectCategories(MarketplaceDbContext dbContext) =>
        dbContext.Categories.AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new AdminCategoryResponse(
                category.Id,
                category.Name,
                category.Slug,
                category.ParentCategoryId,
                category.IsActive,
                category.Products.Count(link =>
                    link.Product.Status == ProductStatus.Active
                    && link.Product.Store.Status == StoreStatus.Active),
                category.CreatedAt,
                category.UpdatedAt));

    private static async Task<Dictionary<string, string[]>> ValidateAsync(
        CategoryAdministrationRequest request,
        Guid? categoryId,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (errors.Count > 0) return errors;

        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await dbContext.Categories.AsNoTracking().AnyAsync(category =>
                (!categoryId.HasValue || category.Id != categoryId.Value)
                && category.Slug.ToLower() == slug,
                cancellationToken))
        {
            errors["Slug"] = ["This category address is already in use."];
        }

        if (request.ParentCategoryId is not Guid parentCategoryId) return errors;

        var hierarchy = await dbContext.Categories.AsNoTracking()
            .Select(category => new CategoryHierarchy(category.Id, category.ParentCategoryId))
            .ToDictionaryAsync(category => category.Id, cancellationToken);
        if (!hierarchy.ContainsKey(parentCategoryId))
        {
            errors["ParentCategoryId"] = ["The selected parent category does not exist."];
            return errors;
        }

        var visited = new HashSet<Guid>();
        Guid? ancestorId = parentCategoryId;
        while (ancestorId is Guid currentId)
        {
            if (categoryId == currentId)
            {
                errors["ParentCategoryId"] = ["A category cannot be nested beneath itself or one of its descendants."];
                break;
            }
            if (!visited.Add(currentId))
            {
                errors["ParentCategoryId"] = ["The selected parent belongs to a category cycle."];
                break;
            }
            if (!hierarchy.TryGetValue(currentId, out var ancestor))
            {
                errors["ParentCategoryId"] = ["The selected parent category does not exist."];
                break;
            }
            ancestorId = ancestor.ParentCategoryId;
        }

        return errors;
    }

    private static void AddAudit(
        MarketplaceDbContext dbContext,
        ClaimsPrincipal principal,
        Category category,
        string action,
        HttpContext httpContext,
        object changes) => dbContext.AuditEntries.Add(new AuditEntry
    {
        UserId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
        EntityType = nameof(Category),
        EntityId = category.Id.ToString(),
        Action = action,
        ChangesJson = JsonSerializer.Serialize(changes),
        CorrelationId = httpContext.TraceIdentifier,
        OccurredAt = DateTimeOffset.UtcNow
    });

    private sealed record CategoryHierarchy(Guid Id, Guid? ParentCategoryId);
}
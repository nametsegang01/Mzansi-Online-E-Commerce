using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class StaffAdministrationEndpoints
{
    private static readonly HashSet<string> StaffRoles = new(
        [AppRoles.ProductAdministrator, AppRoles.FulfilmentEmployee, AppRoles.BusinessManager],
        StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapStaffAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/admin/staff")
            .WithTags("Staff administration")
            .RequireAuthorization(AuthorizationPolicies.UserAdministration)
            .MapPost("/", ProvisionAsync)
            .Produces<StaffProvisioningResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> ProvisionAsync(
        StaffProvisioningRequest request,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (!StaffRoles.Contains(request.Role))
        {
            errors["Role"] = ["Choose ProductAdministrator, FulfilmentEmployee, or BusinessManager."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            Status = AccountStatus.Active
        };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return Results.ValidationProblem(EndpointValidation.FromIdentity(createResult));
        }

        var roleResult = await userManager.AddToRoleAsync(user, request.Role);
        if (!roleResult.Succeeded)
        {
            if (transaction is null) await userManager.DeleteAsync(user);
            return Results.ValidationProblem(EndpointValidation.FromIdentity(roleResult));
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            UserId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id.ToString(),
            Action = "StaffAccountProvisioned",
            ChangesJson = JsonSerializer.Serialize(new { role = request.Role }),
            CorrelationId = httpContext.TraceIdentifier,
            OccurredAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        var response = new StaffProvisioningResponse(
            user.Id,
            user.Email!,
            user.DisplayName,
            [request.Role],
            user.Status.ToString());
        return Results.Created("/api/admin/staff", response);
    }
}
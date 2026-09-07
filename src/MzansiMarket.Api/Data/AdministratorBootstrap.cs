using Microsoft.AspNetCore.Identity;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Data;

public static class AdministratorBootstrap
{
    public static async Task EnsureAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password)) return;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("BootstrapAdmin requires both Email and Password.");
        }

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (await userManager.IsInRoleAsync(existing, AppRoles.SystemAdministrator)) return;
            throw new InvalidOperationException("The configured bootstrap administrator email is already assigned to a non-administrator account.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = configuration["BootstrapAdmin:DisplayName"]?.Trim() is { Length: > 0 } displayName
                ? displayName
                : "Mzansi Market Administrator",
            Status = AccountStatus.Active
        };
        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException($"The bootstrap administrator could not be created: {string.Join(", ", created.Errors.Select(error => error.Code))}.");
        }

        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.SystemAdministrator);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            throw new InvalidOperationException($"The bootstrap administrator role could not be assigned: {string.Join(", ", roleResult.Errors.Select(error => error.Code))}.");
        }

        var database = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        database.AuditEntries.Add(new AuditEntry
        {
            UserId = user.Id,
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id.ToString(),
            Action = "BootstrapAdministratorCreated",
            ChangesJson = "{\"role\":\"SystemAdministrator\"}",
            CorrelationId = "application-startup",
            OccurredAt = DateTimeOffset.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("The configured bootstrap administrator account is ready.");
    }
}

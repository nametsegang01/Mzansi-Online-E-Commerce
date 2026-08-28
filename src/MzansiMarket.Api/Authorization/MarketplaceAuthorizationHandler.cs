using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Authorization;

public sealed class MarketplaceAuthorizationHandler(
    UserManager<ApplicationUser> userManager,
    MarketplaceDbContext dbContext)
    : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        var requirements = context.PendingRequirements.ToArray();
        if (!TryGetUserId(context.User, out var userId))
        {
            return;
        }

        ApplicationUser? user = null;

        foreach (var requirement in requirements)
        {
            if (requirement is ActiveAccountRequirement)
            {
                user ??= await userManager.FindByIdAsync(userId.ToString());
                if (user?.Status == AccountStatus.Active)
                {
                    context.Succeed(requirement);
                }
            }

            if (requirement is ApprovedSellerRequirement
                && context.User.IsInRole(AppRoles.Seller)
                && await IsApprovedSellerAsync(userId))
            {
                context.Succeed(requirement);
            }

            if (requirement is MarketplacePermissionRequirement permission
                && (permission.StaffRoles.Any(context.User.IsInRole)
                    || permission.AllowApprovedSeller
                    && context.User.IsInRole(AppRoles.Seller)
                    && await IsApprovedSellerAsync(userId)))
            {
                context.Succeed(requirement);
            }
        }

        async Task<bool> IsApprovedSellerAsync(Guid id) =>
            await dbContext.SellerProfiles.AsNoTracking().AnyAsync(
                seller => seller.UserId == id && seller.Status == SellerStatus.Approved);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}

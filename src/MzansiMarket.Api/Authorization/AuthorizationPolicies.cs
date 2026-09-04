using Microsoft.AspNetCore.Authorization;

namespace MzansiMarket.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string ActiveAccount = "ActiveAccount";
    public const string ApprovedSeller = "ApprovedSeller";
    public const string SellerWorkspace = "SellerWorkspace";
    public const string CustomerAccess = "CustomerAccess";
    public const string CatalogueManagement = "CatalogueManagement";
    public const string Fulfilment = "Fulfilment";
    public const string ManagementReports = "ManagementReports";
    public const string UserAdministration = "UserAdministration";

    public static void Configure(AuthorizationOptions options)
    {
        var activeAccountPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveAccountRequirement())
            .Build();

        options.FallbackPolicy = activeAccountPolicy;
        options.AddPolicy(ActiveAccount, activeAccountPolicy);
        options.AddPolicy(ApprovedSeller, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveAccountRequirement(), new ApprovedSellerRequirement()));
        options.AddPolicy(SellerWorkspace, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveAccountRequirement())
            .RequireRole(AppRoles.Seller));
        options.AddPolicy(CustomerAccess, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveAccountRequirement())
            .RequireRole(AppRoles.Customer));
        options.AddPolicy(CatalogueManagement, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(
                new ActiveAccountRequirement(),
                new MarketplacePermissionRequirement(
                    [AppRoles.ProductAdministrator, AppRoles.SystemAdministrator],
                    AllowApprovedSeller: true)));
        options.AddPolicy(Fulfilment, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(
                new ActiveAccountRequirement(),
                new MarketplacePermissionRequirement(
                    [AppRoles.FulfilmentEmployee, AppRoles.SystemAdministrator],
                    AllowApprovedSeller: true)));
        options.AddPolicy(ManagementReports, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveAccountRequirement())
            .RequireRole(AppRoles.BusinessManager, AppRoles.SystemAdministrator));
        options.AddPolicy(UserAdministration, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveAccountRequirement())
            .RequireRole(AppRoles.SystemAdministrator));
    }
}

public sealed class ActiveAccountRequirement : IAuthorizationRequirement;

public sealed class ApprovedSellerRequirement : IAuthorizationRequirement;

public sealed record MarketplacePermissionRequirement(
    IReadOnlyCollection<string> StaffRoles,
    bool AllowApprovedSeller) : IAuthorizationRequirement;

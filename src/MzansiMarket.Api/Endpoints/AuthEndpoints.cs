using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/register/customer", RegisterCustomerAsync)
            .AllowAnonymous()
            .RequireRateLimiting("authentication")
            .Produces<RegistrationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPost("/register/seller", RegisterSellerAsync)
            .AllowAnonymous()
            .RequireRateLimiting("authentication")
            .Produces<RegistrationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("authentication")
            .Produces<AccessTokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting("authentication")
            .Produces<AccessTokenResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization(AuthorizationPolicies.ActiveAccount)
            .Produces<CurrentUserResponse>();

        group.MapPost("/logout", () => Results.NoContent())
            .RequireAuthorization(AuthorizationPolicies.ActiveAccount)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/logout-all", LogoutEverywhereAsync)
            .RequireAuthorization(AuthorizationPolicies.ActiveAccount)
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }

    private static async Task<IResult> RegisterCustomerAsync(
        CustomerRegistrationRequest request,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var email = request.Email.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var user = CreateUser(email, $"{firstName} {lastName}");

        return await ExecuteRegistrationAsync(
            user,
            request.Password,
            [AppRoles.Customer],
            async () =>
            {
                dbContext.CustomerProfiles.Add(new CustomerProfile
                {
                    UserId = user.Id,
                    FirstName = firstName,
                    LastName = lastName,
                    MobileNumber = NullIfWhiteSpace(request.MobileNumber)
                });
                await Task.CompletedTask;
            },
            sellerStatus: null,
            storeSlug: null,
            userManager,
            dbContext,
            httpContext,
            cancellationToken);
    }

    private static async Task<IResult> RegisterSellerAsync(
        SellerRegistrationRequest request,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var storeSlug = request.StoreSlug.Trim().ToLowerInvariant();
        if (await dbContext.Stores.IgnoreQueryFilters().AnyAsync(store => store.Slug == storeSlug, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["StoreSlug"] = ["This store address is already in use."]
            });
        }

        var email = request.Email.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var tradingName = request.TradingName.Trim();
        var user = CreateUser(email, $"{firstName} {lastName}");

        return await ExecuteRegistrationAsync(
            user,
            request.Password,
            [AppRoles.Customer, AppRoles.Seller],
            async () =>
            {
                dbContext.CustomerProfiles.Add(new CustomerProfile
                {
                    UserId = user.Id,
                    FirstName = firstName,
                    LastName = lastName,
                    MobileNumber = NullIfWhiteSpace(request.MobileNumber)
                });
                dbContext.SellerProfiles.Add(new SellerProfile
                {
                    UserId = user.Id,
                    TradingName = tradingName,
                    RegistrationNumber = NullIfWhiteSpace(request.RegistrationNumber),
                    Status = SellerStatus.Pending,
                    CommissionRate = 0.10m
                });
                dbContext.Stores.Add(new Store
                {
                    SellerId = user.Id,
                    Name = tradingName,
                    Slug = storeSlug,
                    SupportEmail = NullIfWhiteSpace(request.SupportEmail) ?? email,
                    Status = StoreStatus.Draft
                });
                await Task.CompletedTask;
            },
            SellerStatus.Pending.ToString(),
            storeSlug,
            userManager,
            dbContext,
            httpContext,
            cancellationToken);
    }

    private static async Task<IResult> ExecuteRegistrationAsync(
        ApplicationUser user,
        string password,
        string[] roles,
        Func<Task> addProfiles,
        string? sellerStatus,
        string? storeSlug,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return Results.ValidationProblem(EndpointValidation.FromIdentity(createResult));
        }

        var roleResult = await userManager.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded)
        {
            if (transaction is null)
            {
                await userManager.DeleteAsync(user);
            }

            return Results.ValidationProblem(EndpointValidation.FromIdentity(roleResult));
        }

        await addProfiles();
        dbContext.AuditEntries.Add(new AuditEntry
        {
            UserId = user.Id,
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id.ToString(),
            Action = "Registered",
            ChangesJson = System.Text.Json.JsonSerializer.Serialize(new { roles }),
            CorrelationId = httpContext.TraceIdentifier,
            OccurredAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Results.Created("/api/auth/me", new RegistrationResponse(
            user.Id,
            user.Email!,
            user.DisplayName,
            roles,
            sellerStatus,
            storeSlug));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var errors = EndpointValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user?.Status != AccountStatus.Active)
        {
            return AuthenticationFailed();
        }

        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        var signInResult = await signInManager.PasswordSignInAsync(
            user.UserName!,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        return signInResult.Succeeded ? Results.Empty : AuthenticationFailed();
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        SignInManager<ApplicationUser> signInManager,
        IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
        TimeProvider timeProvider)
    {
        var errors = EndpointValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var refreshTicket = bearerTokenOptions
            .Get(IdentityConstants.BearerScheme)
            .RefreshTokenProtector
            .Unprotect(request.RefreshToken);

        if (refreshTicket?.Properties?.ExpiresUtc is not { } expiresUtc
            || timeProvider.GetUtcNow() >= expiresUtc
            || await signInManager.ValidateSecurityStampAsync(refreshTicket.Principal) is not { Status: AccountStatus.Active } user)
        {
            return Results.Unauthorized();
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        return Results.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        var customer = await dbContext.CustomerProfiles.AsNoTracking()
            .Where(profile => profile.UserId == user.Id)
            .Select(profile => new CustomerProfileResponse(profile.FirstName, profile.LastName, profile.MobileNumber))
            .SingleOrDefaultAsync(cancellationToken);
        var seller = await dbContext.SellerProfiles.AsNoTracking()
            .Where(profile => profile.UserId == user.Id)
            .Select(profile => new SellerProfileResponse(
                profile.TradingName,
                profile.Status.ToString(),
                profile.Store == null ? null : profile.Store.Name,
                profile.Store == null ? null : profile.Store.Slug,
                profile.Store == null ? null : profile.Store.Status.ToString()))
            .SingleOrDefaultAsync(cancellationToken);

        return Results.Ok(new CurrentUserResponse(
            user.Id,
            user.Email!,
            user.DisplayName,
            user.Status.ToString(),
            user.EmailConfirmed,
            roles.ToArray(),
            customer,
            seller));
    }

    private static async Task<IResult> LogoutEverywhereAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            return Results.Problem("The session could not be invalidated.", statusCode: StatusCodes.Status500InternalServerError);
        }

        dbContext.AuditEntries.Add(new AuditEntry
        {
            UserId = user.Id,
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id.ToString(),
            Action = "SecurityStampRotated",
            CorrelationId = httpContext.TraceIdentifier,
            OccurredAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static ApplicationUser CreateUser(string email, string displayName) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        DisplayName = displayName,
        Status = AccountStatus.Active
    };

    private static IResult AuthenticationFailed() => Results.Problem(
        title: "Authentication failed",
        detail: "The email or password is incorrect.",
        statusCode: StatusCodes.Status401Unauthorized);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

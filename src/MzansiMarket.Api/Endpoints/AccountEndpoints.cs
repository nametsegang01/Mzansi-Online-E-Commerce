using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class AccountEndpoints
{
    private static readonly HashSet<string> SouthAfricanProvinces = new(
    [
        "Eastern Cape", "Free State", "Gauteng", "KwaZulu-Natal", "Limpopo",
        "Mpumalanga", "Northern Cape", "North West", "Western Cape"
    ], StringComparer.OrdinalIgnoreCase);

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var account = endpoints.MapGroup("/api/account")
            .WithTags("Account")
            .RequireAuthorization(AuthorizationPolicies.ActiveAccount);

        account.MapGet("/profile", GetProfileAsync).Produces<AccountProfileResponse>();
        account.MapPut("/profile", UpdateProfileAsync)
            .Produces<AccountProfileResponse>()
            .ProducesValidationProblem();
        account.MapPost("/change-email", ChangeEmailAsync)
            .RequireRateLimiting("authentication")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();
        account.MapPost("/change-password", ChangePasswordAsync)
            .RequireRateLimiting("authentication")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        var group = account.MapGroup("/addresses");

        group.MapGet("/", GetAddressesAsync).Produces<IReadOnlyCollection<AddressResponse>>();
        group.MapPost("/", CreateAddressAsync)
            .Produces<AddressResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        group.MapPut("/{id:guid}", UpdateAddressAsync)
            .Produces<AddressResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
        group.MapDelete("/{id:guid}", DeleteAddressAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetProfileAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        var profileMobile = await dbContext.CustomerProfiles.AsNoTracking()
            .Where(profile => profile.UserId == user.Id)
            .Select(profile => profile.MobileNumber)
            .SingleOrDefaultAsync(cancellationToken);
        return Results.Ok(new AccountProfileResponse(user.DisplayName, user.Email!, user.PhoneNumber ?? profileMobile));
    }

    private static async Task<IResult> UpdateProfileAsync(
        AccountProfileRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (request.DisplayName.Trim().Length < 2)
        {
            errors["DisplayName"] = ["Full name must contain at least two visible characters."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var user = await userManager.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        var displayName = request.DisplayName.Trim();
        var mobileNumber = NullIfWhiteSpace(request.MobileNumber);
        user.DisplayName = displayName;
        var mobileNumberChanged = !string.Equals(user.PhoneNumber, mobileNumber, StringComparison.Ordinal);
        if (mobileNumberChanged)
        {
            user.PhoneNumber = mobileNumber;
            user.PhoneNumberConfirmed = false;
        }
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return Results.ValidationProblem(EndpointValidation.FromIdentity(result));

        var customer = await dbContext.CustomerProfiles.SingleOrDefaultAsync(profile => profile.UserId == user.Id, cancellationToken);
        if (customer is not null)
        {
            customer.MobileNumber = mobileNumber;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
        }
        AddAudit(dbContext, user.Id, "ProfileUpdated", new { mobileNumberChanged }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AccountProfileResponse(displayName, user.Email!, mobileNumber));
    }

    private static async Task<IResult> ChangeEmailAsync(
        ChangeEmailRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var user = await userManager.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["CurrentPassword"] = ["The current password is incorrect."] });
        }

        var newEmail = request.NewEmail.Trim();
        var existing = await userManager.FindByEmailAsync(newEmail);
        if (existing is not null && existing.Id != user.Id)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["NewEmail"] = ["That email address is already in use."] });
        }

        user.Email = newEmail;
        user.UserName = newEmail;
        user.EmailConfirmed = false;
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return Results.ValidationProblem(EndpointValidation.FromIdentity(result));

        AddAudit(dbContext, user.Id, "EmailChanged", new { emailChanged = true }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        MarketplaceDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (request.CurrentPassword == request.NewPassword)
        {
            errors["NewPassword"] = ["Choose a new password that is different from the current password."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var user = await userManager.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var identityErrors = result.Errors.ToArray();
            var currentPasswordWrong = identityErrors.Any(error => error.Code == "PasswordMismatch");
            if (currentPasswordWrong)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["CurrentPassword"] = ["The current password is incorrect."] });
            }
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["NewPassword"] = identityErrors.Select(error => error.Description).Distinct().ToArray()
            });
        }

        AddAudit(dbContext, user.Id, "PasswordChanged", null, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static void AddAudit(
        MarketplaceDbContext dbContext,
        Guid userId,
        string action,
        object? changes,
        HttpContext httpContext) => dbContext.AuditEntries.Add(new AuditEntry
        {
            UserId = userId,
            EntityType = nameof(ApplicationUser),
            EntityId = userId.ToString(),
            Action = action,
            ChangesJson = changes is null ? null : JsonSerializer.Serialize(changes),
            CorrelationId = httpContext.TraceIdentifier,
            OccurredAt = DateTimeOffset.UtcNow
        });

    private static async Task<IResult> GetAddressesAsync(
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        var addresses = await dbContext.Addresses.AsNoTracking()
            .Where(address => address.UserId == userId)
            .OrderByDescending(address => address.IsDefault)
            .ThenBy(address => address.CreatedAt)
            .Select(address => ToResponse(address))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(addresses);
    }

    private static async Task<IResult> CreateAddressAsync(
        AddressRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var type, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        var userId = GetUserId(principal);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var existingAddresses = await dbContext.Addresses
            .Where(address => address.UserId == userId)
            .ToArrayAsync(cancellationToken);
        var makeDefault = request.IsDefault || existingAddresses.Length == 0;
        if (makeDefault)
        {
            foreach (var existing in existingAddresses) existing.IsDefault = false;
        }

        var address = new Address { UserId = userId };
        Apply(address, request, type, makeDefault);
        dbContext.Addresses.Add(address);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return Results.Created($"/api/account/addresses/{address.Id}", ToResponse(address));
    }

    private static async Task<IResult> UpdateAddressAsync(
        Guid id,
        AddressRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var type, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        var userId = GetUserId(principal);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var addresses = await dbContext.Addresses
            .Where(address => address.UserId == userId)
            .ToArrayAsync(cancellationToken);
        var address = addresses.SingleOrDefault(item => item.Id == id);
        if (address is null) return Results.NotFound();

        if (request.IsDefault)
        {
            foreach (var existing in addresses) existing.IsDefault = existing.Id == id;
        }

        var remainsDefault = request.IsDefault || address.IsDefault;
        Apply(address, request, type, remainsDefault);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Results.Ok(ToResponse(address));
    }

    private static async Task<IResult> DeleteAddressAsync(
        Guid id,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var addresses = await dbContext.Addresses
            .Where(address => address.UserId == userId)
            .OrderBy(address => address.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var address = addresses.SingleOrDefault(item => item.Id == id);
        if (address is null) return Results.NotFound();

        dbContext.Addresses.Remove(address);
        if (address.IsDefault && addresses.FirstOrDefault(item => item.Id != id) is { } replacement)
        {
            replacement.IsDefault = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static bool TryValidate(
        AddressRequest request,
        out AddressType type,
        out Dictionary<string, string[]> errors)
    {
        errors = EndpointValidation.Validate(request);
        if (!Enum.TryParse(request.Type, ignoreCase: true, out type))
        {
            errors["Type"] = ["Type must be Shipping, Billing, or Both."];
        }

        if (!SouthAfricanProvinces.Contains(request.Province.Trim()))
        {
            errors["Province"] = ["Province must be one of South Africa's nine provinces."];
        }

        return errors.Count == 0;
    }

    private static void Apply(Address address, AddressRequest request, AddressType type, bool isDefault)
    {
        address.Type = type;
        address.RecipientName = request.RecipientName.Trim();
        address.Line1 = request.Line1.Trim();
        address.Line2 = string.IsNullOrWhiteSpace(request.Line2) ? null : request.Line2.Trim();
        address.City = request.City.Trim();
        address.Province = SouthAfricanProvinces.Single(province =>
            province.Equals(request.Province.Trim(), StringComparison.OrdinalIgnoreCase));
        address.PostalCode = request.PostalCode.Trim();
        address.CountryCode = "ZA";
        address.IsDefault = isDefault;
    }

    private static AddressResponse ToResponse(Address address) => new(
        address.Id,
        address.Type.ToString(),
        address.RecipientName,
        address.Line1,
        address.Line2,
        address.City,
        address.Province,
        address.PostalCode,
        address.CountryCode,
        address.IsDefault);

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

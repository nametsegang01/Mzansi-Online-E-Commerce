using System.Security.Claims;
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
        var group = endpoints.MapGroup("/api/account/addresses")
            .WithTags("Customer account")
            .RequireAuthorization(AuthorizationPolicies.CustomerAccess);

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
}

using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class CustomerRegistrationRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string FirstName { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string LastName { get; init; } = string.Empty;

    [Phone, StringLength(32)]
    public string? MobileNumber { get; init; }
}

public sealed class SellerRegistrationRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string FirstName { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string LastName { get; init; } = string.Empty;

    [Phone, StringLength(32)]
    public string? MobileNumber { get; init; }

    [Required, StringLength(180, MinimumLength = 2)]
    public string TradingName { get; init; } = string.Empty;

    [StringLength(80)]
    public string? RegistrationNumber { get; init; }

    [Required, StringLength(180, MinimumLength = 2)]
    public string StoreSlug { get; init; } = string.Empty;

    [EmailAddress, StringLength(256)]
    public string? SupportEmail { get; init; }
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record RegistrationResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    string? SellerStatus,
    string? StoreSlug);

public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string AccountStatus,
    bool EmailConfirmed,
    IReadOnlyCollection<string> Roles,
    CustomerProfileResponse? Customer,
    SellerProfileResponse? Seller);

public sealed record CustomerProfileResponse(string FirstName, string LastName, string? MobileNumber);

public sealed record SellerProfileResponse(
    string TradingName,
    string Status,
    string? StoreName,
    string? StoreSlug,
    string? StoreStatus);

using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class AddressRequest
{
    [Required]
    public string Type { get; init; } = string.Empty;

    [Required, StringLength(160, MinimumLength = 2)]
    public string RecipientName { get; init; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 2)]
    public string Line1 { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Line2 { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string City { get; init; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    public string Province { get; init; } = string.Empty;

    [Required, RegularExpression("^[0-9]{4}$")]
    public string PostalCode { get; init; } = string.Empty;

    [Required, RegularExpression("^ZA$", ErrorMessage = "Only South African addresses are supported in this version.")]
    public string CountryCode { get; init; } = "ZA";

    public bool IsDefault { get; init; }
}

public sealed record AddressResponse(
    Guid Id,
    string Type,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string Province,
    string PostalCode,
    string CountryCode,
    bool IsDefault);

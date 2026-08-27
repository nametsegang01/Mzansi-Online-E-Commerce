using Microsoft.AspNetCore.Identity;

namespace MzansiMarket.Api.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public CustomerProfile? CustomerProfile { get; set; }
    public SellerProfile? SellerProfile { get; set; }
    public ICollection<Address> Addresses { get; set; } = [];
    public ICollection<Cart> Carts { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
}

public sealed class CustomerProfile
{
    public Guid UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ApplicationUser User { get; set; } = null!;
}

public sealed class SellerProfile
{
    public Guid UserId { get; set; }
    public string TradingName { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string? TaxNumber { get; set; }
    public SellerStatus Status { get; set; } = SellerStatus.Pending;
    public decimal CommissionRate { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Store? Store { get; set; }
    public ICollection<SellerPayout> Payouts { get; set; } = [];
}

public sealed class Address : Entity
{
    public Guid UserId { get; set; }
    public AddressType Type { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "ZA";
    public bool IsDefault { get; set; }
    public ApplicationUser User { get; set; } = null!;
}

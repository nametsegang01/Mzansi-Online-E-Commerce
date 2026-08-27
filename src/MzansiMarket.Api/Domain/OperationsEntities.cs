namespace MzansiMarket.Api.Domain;

public sealed class SellerPayout : Entity
{
    public Guid SellerId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal GrossSales { get; set; }
    public decimal PlatformFees { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetAmount { get; set; }
    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;
    public string? ExternalReference { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public SellerProfile Seller { get; set; } = null!;
    public ICollection<SellerPayoutItem> Items { get; set; } = [];
}

public sealed class SellerPayoutItem
{
    public Guid SellerPayoutId { get; set; }
    public Guid SellerOrderId { get; set; }
    public decimal Amount { get; set; }
    public SellerPayout SellerPayout { get; set; } = null!;
    public SellerOrder SellerOrder { get; set; } = null!;
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? ChangesJson { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public ApplicationUser? User { get; set; }
}

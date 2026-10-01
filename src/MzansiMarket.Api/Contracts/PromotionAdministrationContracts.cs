using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class PromotionAdministrationQuery
{
    [StringLength(32)]
    public string? Status { get; init; }
}

public sealed class PromotionAdministrationRequest
{
    [Required, StringLength(60, MinimumLength = 1)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(180, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(32)]
    public string Type { get; init; } = string.Empty;

    [Required, StringLength(32)]
    public string Status { get; init; } = string.Empty;

    public decimal Value { get; init; }

    public decimal? MinimumOrderAmount { get; init; }

    public int? UsageLimit { get; init; }

    public DateTimeOffset StartsAt { get; init; }

    public DateTimeOffset EndsAt { get; init; }

    public IReadOnlyCollection<Guid> ProductIds { get; init; } = [];
}

public sealed record PromotionAdministrationResponse(
    Guid Id,
    string Code,
    string Name,
    string Type,
    string Status,
    decimal Value,
    decimal? MinimumOrderAmount,
    int? UsageLimit,
    int UsageCount,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyCollection<Guid> ProductIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
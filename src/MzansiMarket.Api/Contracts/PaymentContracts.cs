using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class StartSandboxPaymentRequest
{
    [Required, StringLength(40)]
    public string PaymentMethodType { get; init; } = string.Empty;
}

public sealed class SandboxPaymentEventRequest
{
    [Required, StringLength(160, MinimumLength = 8)]
    public string EventId { get; init; } = string.Empty;

    [Required, StringLength(160)]
    public string ProviderReference { get; init; } = string.Empty;

    [Required, StringLength(40)]
    public string Outcome { get; init; } = string.Empty;
}

public sealed record PaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    string Provider,
    string ProviderReference,
    string PaymentMethodType,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? PaidAt);

public sealed record SandboxPaymentEventResponse(
    string EventId,
    bool Duplicate,
    string PaymentStatus,
    string OrderStatus);

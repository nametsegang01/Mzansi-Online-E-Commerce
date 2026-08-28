using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class PaymentEndpoints
{
    private const string Provider = "MzansiSandbox";
    private static readonly string[] AllowedMethods = ["TestWallet", "SandboxEft"];
    private static readonly string[] AllowedOutcomes = ["Paid", "Failed", "Cancelled"];

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/orders/{orderId:guid}/payments/sandbox", StartPaymentAsync)
            .WithTags("Sandbox payments")
            .RequireAuthorization(AuthorizationPolicies.CustomerAccess)
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .Produces<PaymentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/api/payments/sandbox/events", ProcessEventAsync)
            .WithTags("Sandbox payments")
            .AllowAnonymous()
            .Produces<SandboxPaymentEventResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints;
    }

    private static async Task<IResult> StartPaymentAsync(
        Guid orderId,
        StartSandboxPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        var method = AllowedMethods.FirstOrDefault(candidate =>
            candidate.Equals(request.PaymentMethodType?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (method is null) errors["PaymentMethodType"] = ["Use TestWallet or SandboxEft."];
        idempotencyKey = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 100)
        {
            errors["Idempotency-Key"] = ["Provide an Idempotency-Key header between 8 and 100 characters."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var customerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var existing = await dbContext.PaymentRecords.AsNoTracking().SingleOrDefaultAsync(payment =>
            payment.OrderId == orderId && payment.PaymentKey == idempotencyKey
            && payment.Order.CustomerId == customerId, cancellationToken);
        if (existing is not null) return Results.Ok(ToResponse(existing));

        var order = await dbContext.Orders
            .Include(candidate => candidate.SellerOrders).ThenInclude(sellerOrder => sellerOrder.Items)
                .ThenInclude(item => item.Reservation)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId && candidate.CustomerId == customerId,
                cancellationToken);
        if (order is null) return Results.NotFound();
        if (order.Status != OrderStatus.PendingPayment)
        {
            return Results.Problem("This order cannot accept a new payment.",
                statusCode: StatusCodes.Status409Conflict);
        }
        if (order.SellerOrders.SelectMany(candidate => candidate.Items)
            .Any(item => item.Reservation is null || item.Reservation.Status != StockReservationStatus.Active
                || item.Reservation.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            return Results.Problem("The stock reservation has expired. Create a new order from your cart.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var payment = new PaymentRecord
        {
            Order = order,
            PaymentKey = idempotencyKey,
            Provider = Provider,
            ProviderReference = $"SBX-{Guid.NewGuid():N}".ToUpperInvariant(),
            PaymentMethodType = method!,
            Status = PaymentStatus.Pending,
            Amount = order.GrandTotal,
            Currency = order.Currency
        };
        dbContext.PaymentRecords.Add(payment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var replay = await dbContext.PaymentRecords.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.OrderId == orderId && candidate.PaymentKey == idempotencyKey
                && candidate.Order.CustomerId == customerId, cancellationToken);
            if (replay is not null) return Results.Ok(ToResponse(replay));
            throw;
        }
        return Results.Created($"/api/orders/{orderId}", ToResponse(payment));
    }

    private static async Task<IResult> ProcessEventAsync(
        SandboxPaymentEventRequest request,
        [FromHeader(Name = "X-Sandbox-Webhook-Secret")] string? suppliedSecret,
        MarketplaceDbContext dbContext,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var configuredSecret = configuration["SandboxPayments:WebhookSecret"];
        if (!SecretMatches(configuredSecret, suppliedSecret)) return Results.Unauthorized();

        var errors = EndpointValidation.Validate(request);
        var outcome = AllowedOutcomes.FirstOrDefault(candidate =>
            candidate.Equals(request.Outcome?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (outcome is null) errors["Outcome"] = ["Use Paid, Failed, or Cancelled."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var eventId = request.EventId.Trim();
        var duplicate = await dbContext.PaymentProviderEvents.AsNoTracking().Include(item => item.PaymentRecord)
            .ThenInclude(payment => payment.Order)
            .SingleOrDefaultAsync(item => item.Provider == Provider && item.EventId == eventId, cancellationToken);
        if (duplicate is not null)
        {
            return Results.Ok(new SandboxPaymentEventResponse(
                eventId, true, duplicate.PaymentRecord.Status.ToString(), duplicate.PaymentRecord.Order.Status.ToString()));
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        var payment = await dbContext.PaymentRecords
            .Include(candidate => candidate.Order).ThenInclude(order => order.SellerOrders)
                .ThenInclude(sellerOrder => sellerOrder.Items).ThenInclude(item => item.Reservation)
            .SingleOrDefaultAsync(candidate => candidate.Provider == Provider
                && candidate.ProviderReference == request.ProviderReference.Trim(), cancellationToken);
        if (payment is null) return Results.NotFound();
        if (payment.Status != PaymentStatus.Pending)
        {
            return Results.Problem("This payment is already final.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        var reservations = payment.Order.SellerOrders.SelectMany(candidate => candidate.Items)
            .Select(item => item.Reservation)
            .Where(reservation => reservation is not null && reservation.Status == StockReservationStatus.Active)
            .Cast<StockReservation>()
            .ToArray();
        var effectiveOutcome = outcome!;
        if (effectiveOutcome == "Paid" && (reservations.Length == 0 || reservations.Any(item => item.ExpiresAt <= now)))
        {
            effectiveOutcome = "Failed";
            payment.FailureReason = "Stock reservation expired before payment confirmation.";
        }

        if (effectiveOutcome == "Paid")
        {
            foreach (var reservation in reservations)
            {
                if (!await CommitReservationAsync(dbContext, reservation, payment.OrderId, now, cancellationToken))
                {
                    return Results.Problem("Stock could not be committed for this payment.",
                        statusCode: StatusCodes.Status409Conflict);
                }
                reservation.Status = StockReservationStatus.Committed;
            }
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = now;
            payment.Order.Status = OrderStatus.Paid;
            payment.Order.PaidAt = now;
            foreach (var sellerOrder in payment.Order.SellerOrders)
            {
                sellerOrder.Status = SellerOrderStatus.ReadyForFulfilment;
            }
        }
        else
        {
            foreach (var reservation in reservations)
            {
                if (!await ReleaseReservationAsync(dbContext, reservation, now, cancellationToken))
                {
                    return Results.Problem("Stock could not be released for this payment.",
                        statusCode: StatusCodes.Status409Conflict);
                }
                reservation.Status = StockReservationStatus.Released;
                reservation.ReleasedAt = now;
            }
            payment.Status = effectiveOutcome == "Cancelled" ? PaymentStatus.Cancelled : PaymentStatus.Failed;
            payment.FailureReason ??= effectiveOutcome == "Failed" ? "Sandbox payment failed." : null;
            payment.Order.Status = OrderStatus.Cancelled;
            foreach (var sellerOrder in payment.Order.SellerOrders) sellerOrder.Status = SellerOrderStatus.Cancelled;
        }

        dbContext.PaymentProviderEvents.Add(new PaymentProviderEvent
        {
            PaymentRecord = payment,
            Provider = Provider,
            EventId = eventId,
            EventType = effectiveOutcome,
            ReceivedAt = now
        });
        dbContext.AuditEntries.Add(new AuditEntry
        {
            EntityType = nameof(PaymentRecord),
            EntityId = payment.Id.ToString(),
            Action = $"SandboxPayment{effectiveOutcome}",
            CorrelationId = httpContext.TraceIdentifier,
            OccurredAt = now,
            ChangesJson = JsonSerializer.Serialize(new
            {
                payment.OrderId,
                payment.ProviderReference,
                payment.Amount,
                payment.Currency,
                EventId = eventId
            })
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var replay = await dbContext.PaymentProviderEvents.AsNoTracking()
                .Include(item => item.PaymentRecord).ThenInclude(candidate => candidate.Order)
                .SingleOrDefaultAsync(item => item.Provider == Provider && item.EventId == eventId,
                    cancellationToken);
            if (replay is not null)
            {
                return Results.Ok(new SandboxPaymentEventResponse(
                    eventId, true, replay.PaymentRecord.Status.ToString(), replay.PaymentRecord.Order.Status.ToString()));
            }
            throw;
        }
        return Results.Ok(new SandboxPaymentEventResponse(
            eventId, false, payment.Status.ToString(), payment.Order.Status.ToString()));
    }

    private static async Task<bool> CommitReservationAsync(
        MarketplaceDbContext dbContext,
        StockReservation reservation,
        Guid orderId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            var updated = await dbContext.InventoryItems.Where(inventory =>
                    inventory.ProductId == reservation.ProductId
                    && inventory.ReservedQuantity >= reservation.Quantity
                    && inventory.OnHandQuantity >= reservation.Quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(inventory => inventory.ReservedQuantity,
                        inventory => inventory.ReservedQuantity - reservation.Quantity)
                    .SetProperty(inventory => inventory.OnHandQuantity,
                        inventory => inventory.OnHandQuantity - reservation.Quantity)
                    .SetProperty(inventory => inventory.Version, inventory => inventory.Version + 1)
                    .SetProperty(inventory => inventory.UpdatedAt, now), cancellationToken);
            if (updated != 1) return false;
        }
        else
        {
            var inventory = await dbContext.InventoryItems.SingleAsync(item =>
                item.ProductId == reservation.ProductId, cancellationToken);
            if (inventory.ReservedQuantity < reservation.Quantity || inventory.OnHandQuantity < reservation.Quantity)
                return false;
            inventory.ReservedQuantity -= reservation.Quantity;
            inventory.OnHandQuantity -= reservation.Quantity;
        }
        dbContext.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = reservation.ProductId,
            Type = InventoryTransactionType.Sale,
            QuantityDelta = -reservation.Quantity,
            ReferenceType = nameof(Order),
            ReferenceId = orderId,
            Reason = "Sandbox payment confirmed; reserved stock committed."
        });
        return true;
    }

    private static async Task<bool> ReleaseReservationAsync(
        MarketplaceDbContext dbContext,
        StockReservation reservation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            var updated = await dbContext.InventoryItems.Where(inventory =>
                    inventory.ProductId == reservation.ProductId
                    && inventory.ReservedQuantity >= reservation.Quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(inventory => inventory.ReservedQuantity,
                        inventory => inventory.ReservedQuantity - reservation.Quantity)
                    .SetProperty(inventory => inventory.Version, inventory => inventory.Version + 1)
                    .SetProperty(inventory => inventory.UpdatedAt, now), cancellationToken);
            if (updated != 1) return false;
        }
        else
        {
            var inventory = await dbContext.InventoryItems.SingleAsync(item =>
                item.ProductId == reservation.ProductId, cancellationToken);
            if (inventory.ReservedQuantity < reservation.Quantity) return false;
            inventory.ReservedQuantity -= reservation.Quantity;
        }
        dbContext.InventoryTransactions.Add(new InventoryTransaction
        {
            ProductId = reservation.ProductId,
            Type = InventoryTransactionType.Release,
            QuantityDelta = reservation.Quantity,
            ReferenceType = nameof(StockReservation),
            ReferenceId = reservation.Id,
            Reason = "Sandbox payment did not complete; stock reservation released."
        });
        return true;
    }

    private static bool SecretMatches(string? configuredSecret, string? suppliedSecret)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret) || string.IsNullOrWhiteSpace(suppliedSecret)) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(configuredSecret));
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedSecret));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static PaymentResponse ToResponse(PaymentRecord payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Provider,
        payment.ProviderReference!,
        payment.PaymentMethodType,
        payment.Status.ToString(),
        payment.Amount,
        payment.Currency,
        payment.PaidAt);
}

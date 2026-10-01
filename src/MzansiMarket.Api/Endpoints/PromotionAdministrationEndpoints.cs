using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;
using MzansiMarket.Api.Services;

namespace MzansiMarket.Api.Endpoints;

public static class PromotionAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapPromotionAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/promotions")
            .WithTags("Promotion administration")
            .RequireAuthorization(AuthorizationPolicies.PromotionAdministration);

        group.MapGet("/", GetPromotionsAsync)
            .Produces<IReadOnlyCollection<PromotionAdministrationResponse>>()
            .ProducesValidationProblem();
        group.MapPost("/", CreatePromotionAsync)
            .Produces<PromotionAdministrationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        group.MapPut("/{promotionId:guid}", UpdatePromotionAsync)
            .Produces<PromotionAdministrationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetPromotionsAsync(
        [AsParameters] PromotionAdministrationQuery query,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateStatus(query.Status, "Status");
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var promotions = dbContext.Promotions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            promotions = promotions.Where(promotion => promotion.Status.ToString() == query.Status);
        }
        var records = await promotions
            .Include(promotion => promotion.Products)
            .OrderBy(promotion => promotion.Code)
            .ToArrayAsync(cancellationToken);
        var codes = records.Select(promotion => promotion.Code).ToArray();
        var usageCounts = codes.Length == 0
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : await dbContext.Orders.AsNoTracking()
                .Where(order => order.PromotionCode != null && codes.Contains(order.PromotionCode))
                .GroupBy(order => order.PromotionCode!)
                .Select(group => new { Code = group.Key, Count = group.Count() })
                .ToDictionaryAsync(group => group.Code, group => group.Count, cancellationToken);

        return Results.Ok(records.Select(promotion => ToResponse(
            promotion,
            usageCounts.GetValueOrDefault(promotion.Code))).ToArray());
    }

    private static async Task<IResult> CreatePromotionAsync(
        PromotionAdministrationRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        MarketplaceChangeFeed changes,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var (errors, type, status, code, products) = await ValidateAsync(
            request, null, dbContext, cancellationToken);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var promotion = new Promotion
        {
            Code = code,
            Name = request.Name.Trim(),
            Type = type,
            Status = status,
            Value = request.Value,
            MinimumOrderAmount = request.MinimumOrderAmount,
            UsageLimit = request.UsageLimit,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt
        };
        foreach (var product in products)
        {
            promotion.Products.Add(new PromotionProduct { Promotion = promotion, Product = product });
        }
        dbContext.Promotions.Add(promotion);
        AddAudit(dbContext, principal, promotion, "PromotionCreated", httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        changes.Publish("catalogue");

        return Results.Created($"/api/admin/promotions/{promotion.Id}",
            ToResponse(promotion, 0));
    }

    private static async Task<IResult> UpdatePromotionAsync(
        Guid promotionId,
        PromotionAdministrationRequest request,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        MarketplaceChangeFeed changes,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var promotion = await dbContext.Promotions
            .Include(item => item.Products)
            .SingleOrDefaultAsync(item => item.Id == promotionId, cancellationToken);
        if (promotion is null) return Results.NotFound();

        var (errors, type, status, code, products) = await ValidateAsync(
            request, promotionId, dbContext, cancellationToken);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var currentCode = promotion.Code.ToUpperInvariant();
        if (!string.Equals(code, currentCode, StringComparison.Ordinal))
        {
            var codeWasUsed = await dbContext.Orders.AsNoTracking().AnyAsync(order =>
                order.PromotionCode != null && order.PromotionCode.ToUpper() == currentCode,
                cancellationToken);
            if (codeWasUsed)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Code"] = ["A promotion code cannot be changed after it has been used."]
                });
            }
        }

        var currentUsage = await dbContext.Orders.AsNoTracking().CountAsync(order =>
            order.PromotionCode != null && order.PromotionCode.ToUpper() == currentCode,
            cancellationToken);
        if (request.UsageLimit is { } usageLimit && usageLimit < currentUsage)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["UsageLimit"] = ["The usage limit cannot be lower than the promotion's current usage count."]
            });
        }

        var previous = Snapshot(promotion);
        promotion.Code = code;
        promotion.Name = request.Name.Trim();
        promotion.Type = type;
        promotion.Status = status;
        promotion.Value = request.Value;
        promotion.MinimumOrderAmount = request.MinimumOrderAmount;
        promotion.UsageLimit = request.UsageLimit;
        promotion.StartsAt = request.StartsAt;
        promotion.EndsAt = request.EndsAt;
        dbContext.PromotionProducts.RemoveRange(promotion.Products);
        promotion.Products.Clear();
        foreach (var product in products)
        {
            promotion.Products.Add(new PromotionProduct { Promotion = promotion, Product = product });
        }
        AddAudit(dbContext, principal, promotion, "PromotionUpdated", httpContext, previous);
        await dbContext.SaveChangesAsync(cancellationToken);
        changes.Publish("catalogue");

        return Results.Ok(ToResponse(promotion, currentUsage));
    }

    private static async Task<(Dictionary<string, string[]> Errors, PromotionType Type, PromotionStatus Status,
        string Code, Product[] Products)> ValidateAsync(
        PromotionAdministrationRequest request,
        Guid? promotionId,
        MarketplaceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        var code = request.Code.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code)) errors["Code"] = ["Enter a promotion code."];

        var typeValid = TryParseEnum(request.Type, out PromotionType type);
        if (!typeValid) errors["Type"] = ["Type must be Percentage or FixedAmount."];
        var statusValid = TryParseEnum(request.Status, out PromotionStatus status);
        if (!statusValid) errors["Status"] = ["Status must be Draft, Active, Paused, or Expired."];

        if (typeValid && type == PromotionType.Percentage && (request.Value < 0m || request.Value > 100m))
        {
            errors["Value"] = ["Percentage values must be between 0 and 100."];
        }
        else if (typeValid && type == PromotionType.FixedAmount && request.Value <= 0m)
        {
            errors["Value"] = ["Fixed amount values must be greater than zero."];
        }
        if (request.MinimumOrderAmount < 0m)
            errors["MinimumOrderAmount"] = ["Minimum order amount cannot be negative."];
        if (request.UsageLimit < 0)
            errors["UsageLimit"] = ["Usage limit cannot be negative."];
        if (request.StartsAt == default)
            errors["StartsAt"] = ["Start time is required."];
        if (request.EndsAt == default)
            errors["EndsAt"] = ["End time is required."];
        if (request.EndsAt <= request.StartsAt)
            errors["EndsAt"] = ["End time must be after start time."];

        if (errors.Count > 0) return (errors, type, status, code, []);

        if (await dbContext.Promotions.AsNoTracking().AnyAsync(promotion =>
                (!promotionId.HasValue || promotion.Id != promotionId.Value)
                && promotion.Code.ToUpper() == code,
                cancellationToken))
        {
            errors["Code"] = ["This promotion code is already in use."];
        }

        var productIds = request.ProductIds.Distinct().ToArray();
        var products = productIds.Length == 0
            ? []
            : await dbContext.Products
                .Where(product => productIds.Contains(product.Id) && !product.IsDeleted)
                .ToArrayAsync(cancellationToken);
        if (products.Length != productIds.Length)
        {
            errors["ProductIds"] = ["One or more products do not exist or are archived."];
        }

        return (errors, type, status, code, products);
    }

    private static Dictionary<string, string[]> ValidateStatus(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || TryParseEnum(value, out PromotionStatus _)) return [];
        return new Dictionary<string, string[]> { [field] = ["Status must be Draft, Active, Paused, or Expired."] };
    }

    private static bool TryParseEnum<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        return Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out parsed);
    }

    private static PromotionAdministrationResponse ToResponse(Promotion promotion, int usageCount) => new(
        promotion.Id,
        promotion.Code,
        promotion.Name,
        promotion.Type.ToString(),
        promotion.Status.ToString(),
        promotion.Value,
        promotion.MinimumOrderAmount,
        promotion.UsageLimit,
        usageCount,
        promotion.StartsAt,
        promotion.EndsAt,
        promotion.Products.Select(link => link.ProductId).ToArray(),
        promotion.CreatedAt,
        promotion.UpdatedAt);

    private static object Snapshot(Promotion promotion) => new
    {
        promotion.Code,
        promotion.Name,
        Type = promotion.Type.ToString(),
        Status = promotion.Status.ToString(),
        promotion.Value,
        promotion.MinimumOrderAmount,
        promotion.UsageLimit,
        promotion.StartsAt,
        promotion.EndsAt,
        ProductIds = promotion.Products.Select(link => link.ProductId).ToArray()
    };

    private static void AddAudit(
        MarketplaceDbContext dbContext,
        ClaimsPrincipal principal,
        Promotion promotion,
        string action,
        HttpContext httpContext,
        object? previous = null) => dbContext.AuditEntries.Add(new AuditEntry
    {
        UserId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
        EntityType = nameof(Promotion),
        EntityId = promotion.Id.ToString(),
        Action = action,
        ChangesJson = JsonSerializer.Serialize(new { previous, current = Snapshot(promotion) }),
        CorrelationId = httpContext.TraceIdentifier,
        OccurredAt = DateTimeOffset.UtcNow
    });
}
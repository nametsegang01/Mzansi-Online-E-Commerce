using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MzansiMarket.Api.Authorization;
using MzansiMarket.Api.Contracts;
using MzansiMarket.Api.Data;
using MzansiMarket.Api.Domain;

namespace MzansiMarket.Api.Endpoints;

public static class CheckoutEndpoints
{
    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/checkout", PlaceOrderAsync)
            .WithTags("Checkout")
            .RequireAuthorization(AuthorizationPolicies.CustomerAccess)
            .Produces<CheckoutResponse>(StatusCodes.Status201Created)
            .Produces<CheckoutResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints;
    }

    private static async Task<IResult> PlaceOrderAsync(
        CheckoutRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ClaimsPrincipal principal,
        MarketplaceDbContext dbContext,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = EndpointValidation.Validate(request);
        if (request.AddressId == Guid.Empty) errors["AddressId"] = ["Address ID is required."];
        idempotencyKey = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 100)
        {
            errors["Idempotency-Key"] = ["Provide an Idempotency-Key header between 8 and 100 characters."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var customerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var existing = await LoadOrderAsync(dbContext, customerId, idempotencyKey!, cancellationToken);
        if (existing is not null) return Results.Ok(ToResponse(existing));

        var address = await dbContext.Addresses.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == request.AddressId && candidate.UserId == customerId,
            cancellationToken);
        if (address is null) return Results.NotFound();
        if (address.Type == AddressType.Billing)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["AddressId"] = ["Select a shipping or combined address."]
            });
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        var cart = await dbContext.Carts
            .Include(candidate => candidate.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Inventory)
            .Include(candidate => candidate.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Store)
                .ThenInclude(store => store.Seller)
            .Include(candidate => candidate.Items).ThenInclude(item => item.Product).ThenInclude(product => product.Categories)
                .ThenInclude(link => link.Category)
            .SingleOrDefaultAsync(candidate => candidate.CustomerId == customerId && candidate.Status == CartStatus.Active,
                cancellationToken);

        if (cart is null || cart.Items.Count == 0)
        {
            return Results.Problem("Your cart is empty.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        var unavailable = cart.Items.FirstOrDefault(item =>
            item.Quantity <= 0
            || item.Product.Status != ProductStatus.Active
            || item.Product.Store.Status != StoreStatus.Active
            || item.Product.Store.Seller.Status != SellerStatus.Approved
            || !item.Product.Categories.Any(link => link.Category.IsActive)
            || item.Product.Inventory.OnHandQuantity - item.Product.Inventory.ReservedQuantity < item.Quantity);
        if (unavailable is not null)
        {
            return Results.Problem(
                title: "Cart changed",
                detail: $"{unavailable.Product.Name} is unavailable or does not have enough stock.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var promotionResult = await ResolvePromotionAsync(
            dbContext, request.PromotionCode, cart.Items, now, cancellationToken);
        if (promotionResult.Error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["PromotionCode"] = [promotionResult.Error]
            });
        }

        var deliveryFee = configuration.GetValue<decimal?>("Checkout:DeliveryFeePerStore") ?? 75m;
        var freeDeliveryThreshold = configuration.GetValue<decimal?>("Checkout:FreeDeliveryThresholdPerStore") ?? 1000m;
        var reservationMinutes = configuration.GetValue<int?>("Checkout:ReservationMinutes") ?? 15;
        var reservationExpiresAt = now.AddMinutes(Math.Clamp(reservationMinutes, 5, 60));
        var discounts = CalculateDiscounts(cart.Items, promotionResult.Promotion);

        var order = new Order
        {
            OrderNumber = $"MM-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..28].ToUpperInvariant(),
            CheckoutKey = idempotencyKey!,
            PromotionCode = promotionResult.Promotion?.Code,
            CustomerId = customerId,
            Status = OrderStatus.PendingPayment,
            Currency = "ZAR",
            PlacedAt = now,
            ShippingAddress = new OrderAddress
            {
                RecipientName = address.RecipientName,
                Line1 = address.Line1,
                Line2 = address.Line2,
                City = address.City,
                Province = address.Province,
                PostalCode = address.PostalCode,
                CountryCode = address.CountryCode
            }
        };

        foreach (var storeGroup in cart.Items.GroupBy(item => item.Product.Store))
        {
            var storeSubtotal = storeGroup.Sum(item => item.Product.Price * item.Quantity);
            var storeDiscount = storeGroup.Sum(item => discounts[item.Id]);
            var storeDelivery = storeSubtotal - storeDiscount >= freeDeliveryThreshold ? 0m : deliveryFee;
            var commission = decimal.Round((storeSubtotal - storeDiscount)
                * storeGroup.Key.Seller.CommissionRate, 2, MidpointRounding.AwayFromZero);
            var sellerOrder = new SellerOrder
            {
                SellerId = storeGroup.Key.SellerId,
                StoreId = storeGroup.Key.Id,
                Store = storeGroup.Key,
                Status = SellerOrderStatus.Pending,
                Subtotal = storeSubtotal,
                DiscountTotal = storeDiscount,
                DeliveryTotal = storeDelivery,
                CommissionAmount = commission,
                SellerNetAmount = storeSubtotal - storeDiscount - commission
            };

            foreach (var cartItem in storeGroup)
            {
                var discount = discounts[cartItem.Id];
                var orderItem = new OrderItem
                {
                    ProductId = cartItem.ProductId,
                    Product = cartItem.Product,
                    SkuSnapshot = cartItem.Product.Sku,
                    ProductNameSnapshot = cartItem.Product.Name,
                    Quantity = cartItem.Quantity,
                    UnitPrice = cartItem.Product.Price,
                    DiscountAmount = discount,
                    LineTotal = cartItem.Product.Price * cartItem.Quantity - discount
                };
                orderItem.Reservation = new StockReservation
                {
                    ProductId = cartItem.ProductId,
                    Quantity = cartItem.Quantity,
                    Status = StockReservationStatus.Active,
                    ExpiresAt = reservationExpiresAt
                };
                sellerOrder.Items.Add(orderItem);
            }
            order.SellerOrders.Add(sellerOrder);
        }

        order.Subtotal = order.SellerOrders.Sum(candidate => candidate.Subtotal);
        order.DiscountTotal = order.SellerOrders.Sum(candidate => candidate.DiscountTotal);
        order.DeliveryTotal = order.SellerOrders.Sum(candidate => candidate.DeliveryTotal);
        order.GrandTotal = order.Subtotal - order.DiscountTotal + order.DeliveryTotal;

        foreach (var cartItem in cart.Items)
        {
            if (dbContext.Database.IsRelational())
            {
                var updated = await dbContext.InventoryItems
                    .Where(inventory => inventory.ProductId == cartItem.ProductId
                        && inventory.OnHandQuantity - inventory.ReservedQuantity >= cartItem.Quantity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(inventory => inventory.ReservedQuantity,
                            inventory => inventory.ReservedQuantity + cartItem.Quantity)
                        .SetProperty(inventory => inventory.Version, inventory => inventory.Version + 1)
                        .SetProperty(inventory => inventory.UpdatedAt, now), cancellationToken);
                if (updated != 1)
                {
                    return Results.Problem("Stock changed during checkout. Review your cart and retry with a new idempotency key.",
                        statusCode: StatusCodes.Status409Conflict);
                }
                dbContext.Entry(cartItem.Product.Inventory).State = EntityState.Detached;
            }
            else
            {
                cartItem.Product.Inventory.ReservedQuantity += cartItem.Quantity;
            }
        }

        cart.Status = CartStatus.Converted;
        dbContext.Orders.Add(order);
        dbContext.AuditEntries.Add(new AuditEntry
        {
            UserId = customerId,
            EntityType = nameof(Order),
            EntityId = order.Id.ToString(),
            Action = "CheckoutPlaced",
            CorrelationId = httpContext.TraceIdentifier,
            OccurredAt = now,
            ChangesJson = JsonSerializer.Serialize(new
            {
                order.OrderNumber,
                order.Subtotal,
                order.DiscountTotal,
                order.DeliveryTotal,
                order.GrandTotal,
                ReservationExpiresAt = reservationExpiresAt
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
            var duplicate = await LoadOrderAsync(dbContext, customerId, idempotencyKey!, cancellationToken);
            if (duplicate is not null) return Results.Ok(ToResponse(duplicate));
            throw;
        }

        return Results.Created($"/api/orders/{order.Id}", ToResponse(order));
    }

    private static async Task<(Promotion? Promotion, string? Error)> ResolvePromotionAsync(
        MarketplaceDbContext dbContext,
        string? requestedCode,
        ICollection<CartItem> items,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var code = requestedCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return (null, null);

        var promotion = await dbContext.Promotions
            .Include(candidate => candidate.Products)
            .SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);
        if (promotion is null || promotion.Status != PromotionStatus.Active
            || promotion.StartsAt > now || promotion.EndsAt <= now)
        {
            return (null, "The promotion code is invalid or inactive.");
        }
        if (promotion.Type == PromotionType.Percentage && promotion.Value > 100m)
        {
            return (null, "The promotion is not configured correctly.");
        }

        var eligible = EligibleItems(items, promotion).ToArray();
        var eligibleSubtotal = eligible.Sum(item => item.Product.Price * item.Quantity);
        if (eligible.Length == 0 || eligibleSubtotal < (promotion.MinimumOrderAmount ?? 0m))
        {
            return (null, "The cart does not meet this promotion's requirements.");
        }
        if (promotion.UsageLimit is { } limit)
        {
            var uses = await dbContext.Orders.CountAsync(order => order.PromotionCode == promotion.Code, cancellationToken);
            if (uses >= limit) return (null, "The promotion usage limit has been reached.");
        }
        return (promotion, null);
    }

    private static Dictionary<Guid, decimal> CalculateDiscounts(
        ICollection<CartItem> items,
        Promotion? promotion)
    {
        var result = items.ToDictionary(item => item.Id, _ => 0m);
        if (promotion is null) return result;
        var eligible = EligibleItems(items, promotion).ToArray();
        var subtotal = eligible.Sum(item => item.Product.Price * item.Quantity);
        var totalDiscount = promotion.Type == PromotionType.Percentage
            ? decimal.Round(subtotal * promotion.Value / 100m, 2, MidpointRounding.AwayFromZero)
            : Math.Min(subtotal, promotion.Value);
        var remaining = totalDiscount;
        for (var index = 0; index < eligible.Length; index++)
        {
            var item = eligible[index];
            var discount = index == eligible.Length - 1
                ? remaining
                : decimal.Round(totalDiscount * (item.Product.Price * item.Quantity) / subtotal,
                    2, MidpointRounding.AwayFromZero);
            result[item.Id] = discount;
            remaining -= discount;
        }
        return result;
    }

    private static IEnumerable<CartItem> EligibleItems(IEnumerable<CartItem> items, Promotion promotion) =>
        items.Where(item =>
            (promotion.SellerId is null || item.Product.Store.SellerId == promotion.SellerId)
            && (promotion.Products.Count == 0 || promotion.Products.Any(link => link.ProductId == item.ProductId)));

    private static async Task<Order?> LoadOrderAsync(
        MarketplaceDbContext dbContext,
        Guid customerId,
        string checkoutKey,
        CancellationToken cancellationToken) =>
        await dbContext.Orders.AsNoTracking()
            .Include(order => order.ShippingAddress)
            .Include(order => order.SellerOrders).ThenInclude(sellerOrder => sellerOrder.Store)
            .Include(order => order.SellerOrders).ThenInclude(sellerOrder => sellerOrder.Items)
                .ThenInclude(item => item.Reservation)
            .SingleOrDefaultAsync(order => order.CustomerId == customerId && order.CheckoutKey == checkoutKey,
                cancellationToken);

    private static CheckoutResponse ToResponse(Order order)
    {
        var expiresAt = order.SellerOrders.SelectMany(candidate => candidate.Items)
            .Select(item => item.Reservation?.ExpiresAt ?? order.PlacedAt ?? order.CreatedAt)
            .DefaultIfEmpty(order.PlacedAt ?? order.CreatedAt)
            .Min();
        return new CheckoutResponse(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.Subtotal,
            order.DiscountTotal,
            order.DeliveryTotal,
            order.GrandTotal,
            order.Currency,
            expiresAt,
            order.PromotionCode,
            new CheckoutAddressResponse(
                order.ShippingAddress.RecipientName,
                order.ShippingAddress.Line1,
                order.ShippingAddress.Line2,
                order.ShippingAddress.City,
                order.ShippingAddress.Province,
                order.ShippingAddress.PostalCode,
                order.ShippingAddress.CountryCode),
            order.SellerOrders.OrderBy(candidate => candidate.Store.Name).Select(sellerOrder =>
                new CheckoutSellerOrderResponse(
                    sellerOrder.Id,
                    sellerOrder.StoreId,
                    sellerOrder.Store.Name,
                    sellerOrder.Subtotal,
                    sellerOrder.DiscountTotal,
                    sellerOrder.DeliveryTotal,
                    sellerOrder.Subtotal - sellerOrder.DiscountTotal + sellerOrder.DeliveryTotal,
                    sellerOrder.Items.Select(item => new CheckoutItemResponse(
                        item.Id,
                        item.ProductId,
                        item.SkuSnapshot,
                        item.ProductNameSnapshot,
                        item.Quantity,
                        item.UnitPrice,
                        item.DiscountAmount,
                        item.LineTotal)).ToArray())).ToArray());
    }
}

namespace MzansiMarket.Api.Domain;

public sealed class Cart : Entity
{
    public Guid CustomerId { get; set; }
    public CartStatus Status { get; set; } = CartStatus.Active;
    public DateTimeOffset? ExpiresAt { get; set; }
    public ApplicationUser Customer { get; set; } = null!;
    public ICollection<CartItem> Items { get; set; } = [];
}

public sealed class CartItem : Entity
{
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public Cart Cart { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public sealed class Order : Entity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal DeliveryTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string Currency { get; set; } = "ZAR";
    public DateTimeOffset? PlacedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public ApplicationUser Customer { get; set; } = null!;
    public OrderAddress ShippingAddress { get; set; } = null!;
    public ICollection<SellerOrder> SellerOrders { get; set; } = [];
    public ICollection<PaymentRecord> Payments { get; set; } = [];
}

public sealed class OrderAddress
{
    public Guid OrderId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "ZA";
    public Order Order { get; set; } = null!;
}

public sealed class SellerOrder : Entity
{
    public Guid OrderId { get; set; }
    public Guid SellerId { get; set; }
    public Guid StoreId { get; set; }
    public SellerOrderStatus Status { get; set; } = SellerOrderStatus.Pending;
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal DeliveryTotal { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal SellerNetAmount { get; set; }
    public Order Order { get; set; } = null!;
    public SellerProfile Seller { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<Shipment> Shipments { get; set; } = [];
    public ICollection<SellerPayoutItem> PayoutItems { get; set; } = [];
}

public sealed class OrderItem : Entity
{
    public Guid SellerOrderId { get; set; }
    public Guid ProductId { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
    public SellerOrder SellerOrder { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public StockReservation? Reservation { get; set; }
    public ICollection<ReturnRequest> ReturnRequests { get; set; } = [];
}

public sealed class StockReservation : Entity
{
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public StockReservationStatus Status { get; set; } = StockReservationStatus.Active;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public InventoryItem InventoryItem { get; set; } = null!;
}

public sealed class PaymentRecord : Entity
{
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public string PaymentMethodType { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string? FailureReason { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Order Order { get; set; } = null!;
    public ICollection<RefundRecord> Refunds { get; set; } = [];
}

public sealed class Shipment : Entity
{
    public Guid SellerOrderId { get; set; }
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public SellerOrder SellerOrder { get; set; } = null!;
}

public sealed class ReturnRequest : Entity
{
    public Guid OrderItemId { get; set; }
    public Guid CustomerId { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Details { get; set; }
    public ReturnStatus Status { get; set; } = ReturnStatus.Requested;
    public decimal RefundAmount { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public ApplicationUser Customer { get; set; } = null!;
    public ICollection<RefundRecord> Refunds { get; set; } = [];
}

public sealed class RefundRecord : Entity
{
    public Guid ReturnRequestId { get; set; }
    public Guid? PaymentRecordId { get; set; }
    public string? ProviderReference { get; set; }
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public decimal Amount { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public ReturnRequest ReturnRequest { get; set; } = null!;
    public PaymentRecord? PaymentRecord { get; set; }
}

public sealed class Promotion : Entity
{
    public Guid? SellerId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PromotionType Type { get; set; }
    public PromotionStatus Status { get; set; } = PromotionStatus.Draft;
    public decimal Value { get; set; }
    public decimal? MinimumOrderAmount { get; set; }
    public int? UsageLimit { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public SellerProfile? Seller { get; set; }
    public ICollection<PromotionProduct> Products { get; set; } = [];
}

public sealed class PromotionProduct
{
    public Guid PromotionId { get; set; }
    public Guid ProductId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

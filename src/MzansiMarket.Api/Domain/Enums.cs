namespace MzansiMarket.Api.Domain;

public enum AccountStatus { Active, Suspended, Deactivated }
public enum SellerStatus { Pending, Approved, Suspended, Rejected }
public enum StoreStatus { Draft, Active, Suspended, Closed }
public enum ProductStatus { Draft, Active, Inactive, Archived }
public enum AddressType { Shipping, Billing, Both }
public enum CartStatus { Active, Converted, Abandoned, Expired }
public enum OrderStatus { PendingPayment, Paid, Processing, PartiallyShipped, Shipped, Delivered, Cancelled, PartiallyRefunded, Refunded }
public enum SellerOrderStatus { Pending, ReadyForFulfilment, Picking, Packed, Shipped, Delivered, Cancelled, Returned }
public enum PaymentStatus { Pending, Authorized, Paid, Failed, Cancelled, PartiallyRefunded, Refunded }
public enum ShipmentStatus { Pending, Packed, Dispatched, InTransit, Delivered, Failed, Returned }
public enum ReturnStatus { Requested, Approved, Rejected, Received, Refunded, Closed }
public enum RefundStatus { Pending, Submitted, Paid, Failed, Cancelled }
public enum PromotionType { Percentage, FixedAmount }
public enum PromotionStatus { Draft, Active, Paused, Expired }
public enum PayoutStatus { Pending, Processing, Paid, Failed, Held }
public enum InventoryTransactionType { InitialStock, Restock, Reservation, Release, Sale, Return, Adjustment }
public enum StockReservationStatus { Active, Committed, Released, Expired }

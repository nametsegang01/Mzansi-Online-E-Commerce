# Data dictionary

## Conventions

- `uuid` identifiers are database-generated unless they are a foreign/composite key.
- Marketplace entities normally include `Id uuid PK`, `CreatedAt timestamptz`, and `UpdatedAt timestamptz`.
- Money uses `numeric(18,2)`; currency is a three-character ISO code and defaults to `ZAR`.
- Status/type values are stored as bounded text and mapped to C# enums.
- `?` means nullable. `UQ` means unique. All foreign keys are indexed where needed for lookup or lifecycle processing.

## Identity and parties

| Table | Fields | Keys and rules | Purpose |
|---|---|---|---|
| `identity.Users` | Identity fields; `DisplayName`; `Status`; timestamps | `Id PK`; normalized username/email indexes | Authentication account shared by all actor types |
| `identity.Roles` | Identity role fields | `Id PK`; normalized name UQ; six seeded roles | Role-based authorization |
| `identity.UserRoles` | `UserId`, `RoleId` | composite PK; FKs to users/roles | Many-to-many account roles |
| `identity.UserClaims` | Identity claim fields | integer PK; user FK | User authorization claims |
| `identity.UserLogins` | login provider/key/display name, user | composite PK; user FK | External logins |
| `identity.UserTokens` | user/provider/name/value | composite PK; user FK | Identity tokens—not payment-card data |
| `identity.RoleClaims` | role and claim fields | integer PK; role FK | Role claims |
| `marketplace.CustomerProfiles` | `UserId`; `FirstName`; `LastName`; `MobileNumber?`; timestamps | `UserId PK/FK` | Customer-specific profile |
| `marketplace.SellerProfiles` | `UserId`; `TradingName`; `RegistrationNumber?`; `TaxNumber?`; `Status`; `CommissionRate`; `ApprovedAt?`; timestamps | `UserId PK/FK`; commission 0..1 | Seller onboarding and commercial terms |
| `marketplace.Addresses` | common fields; `UserId`; `Type`; recipient and postal fields; `CountryCode`; `IsDefault` | user FK; `(UserId,IsDefault)` index | Reusable customer/seller address book |
| `audit.AuditEntries` | `Id bigint`; `UserId?`; `EntityType`; `EntityId`; `Action`; `ChangesJson jsonb?`; `CorrelationId?`; `OccurredAt` | identity PK; optional user FK; entity/time indexes | Security and business audit trail |

## Seller catalogue and stock

| Table | Fields | Keys and rules | Purpose |
|---|---|---|---|
| `marketplace.Stores` | common fields; `SellerId`; `Name`; `Slug`; `Description?`; `SupportEmail?`; `Status` | seller FK/UQ; slug UQ | Seller's storefront; one store per seller in v1 |
| `marketplace.Categories` | common fields; `ParentCategoryId?`; `Name`; `Slug`; `IsActive` | parent self-FK; slug UQ | Hierarchical catalogue classification |
| `marketplace.Products` | common fields; `StoreId`; `Sku`; `Name`; `Slug`; `Description?`; `Price`; `Currency`; `Status`; `IsDeleted` | store FK; SKU UQ; `(StoreId,Slug)` UQ; price >= 0 | Seller-owned product with soft deletion |
| `marketplace.ProductCategories` | `ProductId`; `CategoryId` | composite PK; product/category FKs | Product-category many-to-many link |
| `marketplace.ProductImages` | common fields; `ProductId`; `StorageKey`; `PublicUrl`; `AltText`; `SortOrder`; `IsPrimary` | product FK; storage key UQ; product/sort UQ; sort >= 0 | Object-storage image metadata and accessible text |
| `marketplace.InventoryItems` | `ProductId`; `OnHandQuantity`; `ReservedQuantity`; `ReorderLevel`; `Version`; `UpdatedAt` | product PK/FK; quantities nonnegative; reserved <= on-hand; concurrency token | Current stock position |
| `marketplace.InventoryTransactions` | common fields; `ProductId`; `Type`; `QuantityDelta`; `ReferenceType?`; `ReferenceId?`; `Reason`; `CreatedByUserId?` | inventory FK; delta != 0; product/time index | Append-only stock movement evidence |

## Basket, ordering, payment, and fulfilment

| Table | Fields | Keys and rules | Purpose |
|---|---|---|---|
| `marketplace.Carts` | common fields; `CustomerId`; `Status`; `ExpiresAt?` | customer FK; customer/status index | Customer basket lifecycle |
| `marketplace.CartItems` | common fields; `CartId`; `ProductId`; `Quantity` | cart/product FKs; pair UQ; quantity > 0 | Basket line |
| `marketplace.Orders` | common fields; `OrderNumber`; `CustomerId`; `Status`; `Subtotal`; `DiscountTotal`; `DeliveryTotal`; `GrandTotal`; `Currency`; `PlacedAt?`; `PaidAt?` | order number UQ; customer FK; all totals >= 0 | Platform-wide customer order |
| `marketplace.OrderAddresses` | `OrderId`; recipient and postal snapshot fields; `CountryCode` | order PK/FK | Immutable delivery-address snapshot |
| `marketplace.SellerOrders` | common fields; `OrderId`; `SellerId`; `StoreId`; `Status`; subtotal/discount/delivery/commission/net | order/seller/store FKs; `(OrderId,StoreId)` UQ; amounts >= 0 | Seller-isolated fulfilment and settlement partition |
| `marketplace.OrderItems` | common fields; `SellerOrderId`; `ProductId`; `SkuSnapshot`; `ProductNameSnapshot`; `Quantity`; price/discount/line total | seller-order/product FKs; quantity > 0; amounts >= 0 | Historical purchased line |
| `marketplace.StockReservations` | common fields; `OrderItemId`; `ProductId`; `Quantity`; `Status`; `ExpiresAt`; `ReleasedAt?` | order item UQ/FK; inventory FK; quantity > 0; lifecycle index | Temporary checkout stock hold |
| `marketplace.PaymentRecords` | common fields; `OrderId`; `Provider`; `ProviderReference?`; `PaymentMethodType`; `Status`; `Amount`; `Currency`; `FailureReason?`; `PaidAt?` | order FK; provider/reference UQ when present; amount >= 0 | Sandbox/provider payment outcome; no PAN/CVV |
| `marketplace.Shipments` | common fields; `SellerOrderId`; `Status`; `Carrier?`; `TrackingNumber?`; `DispatchedAt?`; `DeliveredAt?` | seller-order FK; carrier/tracking UQ when present | Pick, pack, dispatch, delivery lifecycle |

## Returns, promotions, and seller settlement

| Table | Fields | Keys and rules | Purpose |
|---|---|---|---|
| `marketplace.ReturnRequests` | common fields; `OrderItemId`; `CustomerId`; `Quantity`; `Reason`; `Details?`; `Status`; `RefundAmount`; `RequestedAt`; `ResolvedAt?` | order-item/customer FKs; quantity > 0; refund >= 0 | Item-level return decision |
| `marketplace.RefundRecords` | common fields; `ReturnRequestId`; `PaymentRecordId?`; `ProviderReference?`; `Status`; `Amount`; `PaidAt?` | return/payment FKs; amount > 0 | Refund linked to the original return and payment |
| `marketplace.Promotions` | common fields; `SellerId?`; `Code`; `Name`; `Type`; `Status`; `Value`; `MinimumOrderAmount?`; `UsageLimit?`; `StartsAt`; `EndsAt` | seller optional FK; code UQ; value > 0; end > start | Platform-wide or seller promotion definition |
| `marketplace.PromotionProducts` | `PromotionId`; `ProductId` | composite PK; promotion/product FKs | Promotion product scope |
| `marketplace.SellerPayouts` | common fields; `SellerId`; period dates; `GrossSales`; `PlatformFees`; `Refunds`; `NetAmount`; `Status`; `ExternalReference?`; `PaidAt?` | seller FK; seller/period UQ; external ref UQ when present; dates/amounts checked | Seller settlement batch |
| `marketplace.SellerPayoutItems` | `SellerPayoutId`; `SellerOrderId`; `Amount` | composite PK; payout/order FKs; amount >= 0 | Traceable payout-to-seller-order allocation |

## Relationship and deletion policy

Owned transient children such as cart items, product images, order address snapshots, and seller-order lines cascade with their parent. Referenced business history—products used in orders, payments, returns, refunds, inventory movements, and payouts—uses restricted deletion so financial and audit evidence cannot disappear accidentally. Optional audit actors use `SET NULL` to preserve the event if an account is later removed.

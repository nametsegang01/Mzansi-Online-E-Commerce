# Entity relationship diagram

The diagram emphasizes the marketplace transaction path. ASP.NET Identity support tables (`UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`) are omitted visually but remain in the migration and data dictionary.

```mermaid
erDiagram
    USERS ||--o| CUSTOMER_PROFILES : has
    USERS ||--o| SELLER_PROFILES : has
    USERS ||--o{ ADDRESSES : saves
    USERS }o--o{ ROLES : assigned
    SELLER_PROFILES ||--|| STORES : operates
    STORES ||--o{ PRODUCTS : lists
    PRODUCTS ||--|| INVENTORY_ITEMS : stocked_by
    INVENTORY_ITEMS ||--o{ INVENTORY_TRANSACTIONS : records
    PRODUCTS }o--o{ CATEGORIES : classified_as
    PRODUCTS ||--o{ PRODUCT_IMAGES : displays
    USERS ||--o{ CARTS : owns
    CARTS ||--o{ CART_ITEMS : contains
    PRODUCTS ||--o{ CART_ITEMS : selected_as
    USERS ||--o{ ORDERS : places
    ORDERS ||--|| ORDER_ADDRESSES : snapshots
    ORDERS ||--o{ SELLER_ORDERS : splits_into
    STORES ||--o{ SELLER_ORDERS : fulfils
    SELLER_PROFILES ||--o{ SELLER_ORDERS : earns_from
    SELLER_ORDERS ||--|{ ORDER_ITEMS : contains
    PRODUCTS ||--o{ ORDER_ITEMS : snapshots
    ORDER_ITEMS ||--o| STOCK_RESERVATIONS : reserves
    INVENTORY_ITEMS ||--o{ STOCK_RESERVATIONS : holds
    ORDERS ||--o{ PAYMENT_RECORDS : paid_by
    SELLER_ORDERS ||--o{ SHIPMENTS : ships_as
    ORDER_ITEMS ||--o{ RETURN_REQUESTS : returned_as
    RETURN_REQUESTS ||--o{ REFUND_RECORDS : resolved_by
    PAYMENT_RECORDS ||--o{ REFUND_RECORDS : reverses
    SELLER_PROFILES o|--o{ PROMOTIONS : sponsors
    PROMOTIONS }o--o{ PRODUCTS : applies_to
    SELLER_PROFILES ||--o{ SELLER_PAYOUTS : receives
    SELLER_PAYOUTS ||--o{ SELLER_PAYOUT_ITEMS : itemizes
    SELLER_ORDERS ||--o{ SELLER_PAYOUT_ITEMS : settles
    USERS o|--o{ AUDIT_ENTRIES : acts_in
```

Key cardinality: one customer order can span many sellers, but each `SellerOrder` belongs to exactly one store and seller. This prevents one seller from seeing or fulfilling another seller's items.

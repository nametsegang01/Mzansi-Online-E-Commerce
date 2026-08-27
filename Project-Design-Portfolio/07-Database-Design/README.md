# Database design decisions

## Scope

This normalized relational model translates the assignment's marketplace, order fulfilment, promotions, reporting, payment, return, refund, security, and audit requirements into PostgreSQL. The user clarification that people will sell on the platform is modeled explicitly through `SellerProfiles`, `Stores`, seller-owned products, seller-specific fulfilment orders, commissions, and payouts.

## Important decisions

- One login can carry multiple ASP.NET Identity roles. Seeded roles are Customer, Seller, ProductAdministrator, FulfilmentEmployee, BusinessManager, and SystemAdministrator.
- A seller has one store in version 1. The unique `Stores.SellerId` constraint makes that assumption explicit and reversible in a later migration.
- A customer checkout creates one platform `Order` and one `SellerOrder` per participating store. Fulfilment, shipments, commissions, and payouts remain seller-specific.
- Product SKU is globally unique. A seller-specific URL slug is unique inside its store.
- Product images store object-storage keys and URLs, not binary media.
- Order items and shipping addresses preserve snapshots so completed transactions remain historically accurate.
- Inventory uses a concurrency token and an append-only transaction trail. Checkout code must reserve stock in a transaction and retry optimistic-concurrency conflicts.
- Payment records contain provider references and method types only; they never contain PAN/CVV data.
- Operational deletes should normally be status changes. Products use soft deletion; financial, order, refund, payout, and audit history should be retained.

## Application-level invariants

Some rules span multiple rows and therefore belong in transactional services rather than a single row constraint:

- A published product must have at least one active category and an inventory row.
- An order must contain at least one seller order and at least one item.
- The server recalculates all order, commission, payout, promotion, and refund totals.
- Stock reservation and release update inventory, reservation, and transaction rows atomically.
- A refund must not exceed the captured payment or eligible returned quantity.
- Role and ownership authorization protects seller catalogue, price, stock, fulfilment, and payout operations.

# Backend implementation state

## Objective and phase

- Objective: implement the complete Mzansi Market Online application API from the approved assignment baseline.
- Platform: ASP.NET Core on .NET 10, Entity Framework Core, PostgreSQL 18, and Render.
- Current phase: reseller catalogue and administration.
- Safety boundary: use fictional users and sandbox payment references only; never collect or store card numbers, CVV values, identity numbers, or confidential business data.

## Architecture and decisions

- Keep the existing cross-platform ASP.NET Core application. The phrase "ASP.NET Framework" is interpreted as the ASP.NET technology family, not the legacy Windows-only .NET Framework runtime.
- Use one modular API deployment initially, with clear domain/service/endpoint boundaries. Split services only when operational evidence justifies it.
- ASP.NET Core Identity and the existing PostgreSQL `identity` schema are authoritative for users, password hashing, lockout, security stamps, and roles.
- Browser clients use ASP.NET Core Identity bearer access and refresh tokens. Tokens are opaque rather than JWTs and must be treated as secrets by clients.
- Data-protection keys are persisted in PostgreSQL so authentication tokens survive application restarts and Render deployments.
- Role checks are necessary but not sufficient: seller operations also require an approved seller profile and ownership checks.
- Server code remains authoritative for prices, discounts, delivery charges, stock, commission, refund, and payout calculations.
- Stock reservations, order creation, payment-state changes, returns, and refunds use database transactions and append audit evidence.
- The API exposes specific validation problems without exposing stack traces, password details, or account-state information to unauthenticated callers.

## Dependency-ordered work units

1. `BE-001 Identity foundation`: customer/seller registration, login, refresh, account inspection, logout-all, lockout, role policies, active-account checks, approved-seller checks, CORS, rate limiting, persisted data-protection keys, and API tests.
2. `BE-002 Public catalogue`: categories, product search/filter/paging, product detail, availability, seller/store context, and query tests.
3. `BE-003 Customer account and cart`: address book, one active cart, add/update/remove items, server-derived cart summaries, ownership checks, and tests.
4. `BE-004 Transactional checkout`: address snapshots, promotion evaluation, delivery calculation, multi-seller order creation, concurrency-safe stock reservations, idempotency, and tests.
5. `BE-005 Sandbox payments`: simulated provider adapter, payment-status webhook simulation, duplicate-event protection, reservation commit/release, and tests. No raw card fields.
6. `BE-006 Fulfilment`: paid-order queues, seller/employee permissions, approved status transitions, picking, packing, shipment tracking, and audit events.
7. `BE-007 Cancellation, returns, and refunds`: eligibility rules, quantity limits, manager approval where required, sandbox refund records, stock adjustment, and tests.
8. `BE-008 Seller and staff administration`: seller approval, catalogue, categories, prices, images, stock, promotions, role administration, ownership enforcement, and tests.
9. `BE-009 Reporting and audit`: sales, stock, fulfilment, seller performance, controlled audit access, monitoring, and export-safe responses.
10. `BE-010 Cross-system release`: frontend integration, OpenAPI review, accessibility/security/performance testing, Render deployment, migrations, recovery checks, and demonstration data.

## Validation baseline

- Existing release build passes with zero warnings and errors.
- Existing database-model suite passes 5/5 tests.
- Existing schema contains seller-aware commerce, inventory, payment-reference, returns, refunds, payouts, and audit boundaries.

## Completed and validated work units

### BE-001 Identity foundation

- Acceptance criteria:
  - Customers and sellers can register with validated fictional profile data.
  - Seller registration creates a pending seller profile and draft store without granting approved-seller access.
  - Valid credentials produce short-lived access and refresh tokens; invalid credentials return a generic unauthorized response and trigger lockout accounting.
  - Authenticated callers can retrieve their own account and role information.
  - Suspended/deactivated users and unapproved sellers fail the corresponding authorization policies.
  - Authentication endpoints are rate-limited and the deployed frontend is the only configured production browser origin.
  - Data-protection keys persist in PostgreSQL.
  - Positive, negative, validation, lockout, and permission-focused automated tests pass.

- Status: PASS locally and authentication routes verified on Render.
- Implemented customer and seller registration, opaque bearer access/refresh tokens, current-account inspection, client logout, logout-all security-stamp rotation, password lockout, active-account enforcement, approved-seller enforcement, staff policies, production CORS, and per-client auth throttling.
- Added the additive `20260828054251_PersistDataProtectionKeys` migration and idempotent SQL.
- Validation: 10/10 automated tests passed; release build produced zero warnings/errors; EF reports no pending model changes; NuGet audit reports no vulnerable API packages.
- Release result: DATA-002 is applied and the public registration, login, refresh, current-user, invalid-login, database-health, and production-CORS checks pass.

### BE-002 Public catalogue

- Acceptance criteria:
  - Anonymous users can list active categories and active products from active stores.
  - Product search supports name, description, SKU, category, seller/store, price, availability, sorting, and bounded pagination.
  - Product detail returns seller, category, accessible image, price, and availability data without exposing draft, inactive, deleted, or suspended content.
  - Invalid filters return specific validation problems.
  - Positive, hidden-content, filtering, paging, and not-found API tests pass.

- Status: PASS locally; deployed on Render with the API service.
- Implemented active-category listing; active product paging, search, category/store/price/availability filters and sorting; product detail by ID and store/product slug; accessible primary-image metadata; and strict draft, inactive-category, deleted-product, and suspended-store visibility boundaries.
- Validation: full suite passes 14/14 tests; formatting verification passes; release build remains warning-free.

### BE-003 Customer account and cart

- Acceptance criteria:
  - Active customers can list, create, update, default, and remove only their own South African delivery/billing addresses.
  - Active customers have at most one application-managed active cart.
  - Cart add/update/remove operations enforce positive bounded quantities, active product visibility, current available stock, and ownership.
  - Cart responses recalculate item and subtotal values from current server-side catalogue prices.
  - Anonymous, cross-customer, invalid-quantity, unavailable-stock, inactive-product, and not-found cases are covered by API tests.

- Status: PASS locally; deployed on Render with the API service.
- Implemented owned South African address management, deterministic default-address replacement, one active cart, stock-aware cart mutations, current-price summaries, and database-level partial unique indexes for customer invariants.

### BE-004 Transactional checkout

- Acceptance criteria:
  - A customer must supply an owned shipping-capable address and a bounded idempotency key.
  - Checkout revalidates product/store/seller/category availability and stock inside the operation.
  - The server evaluates active platform/seller/product promotions and calculates every price, discount, per-store delivery charge, commission, and grand total.
  - One platform order and one seller order per participating store are created with immutable address/product snapshots.
  - Conditional inventory updates prevent over-reservation; active reservations expire after a configured interval.
  - Successful replay returns the original order without reserving stock twice.
  - Positive, replay, validation, authentication, invalid-promotion, address-type, and changed-stock cases are tested.

- Status: PASS locally; deployed on Render with the API service.
- Implemented multi-seller order creation, snapshotting, promotion allocation, configurable delivery, seller commission totals, 15-minute stock reservations, cart conversion, audit evidence, and customer-scoped idempotency.
- Validation: full suite passes 20/20 tests at the BE-004 checkpoint; release build remains warning-free.

### BE-005 Sandbox payments

- Acceptance criteria:
  - Only the owning customer can initiate payment for a pending order with active reservations.
  - Payment initiation requires an idempotency key and accepts sandbox method labels only; no raw card fields exist.
  - Provider events require a separately configured secret and accept Paid, Failed, or Cancelled outcomes.
  - Paid events atomically reduce on-hand and reserved stock, commit reservations, append stock movements, mark the order paid, and release seller orders to fulfilment.
  - Failed/cancelled or expired-reservation outcomes release reserved stock and cancel the unpaid order.
  - Repeated payment keys and event IDs return the original state without duplicate financial, stock, or audit effects.

- Status: PASS locally; deployed on Render with the API service.
- Implemented sandbox payment initiation, constant-time webhook-secret verification, minimal provider-event receipts, duplicate handling, payment/order/seller-order transitions, reservation commit/release, inventory evidence, and audit evidence.
- Validation: full suite passes 22/22 tests; release build remains warning-free.

### BE-006 Fulfilment

- Acceptance criteria:
  - Approved sellers see only seller orders owned by their seller identity; fulfilment/system staff can use the shared queue.
  - Queue results contain only operational order, recipient region, item, and shipment fields needed for fulfilment.
  - Status changes follow `ReadyForFulfilment -> Picking -> Packed -> Shipped -> Delivered` and reject skipped or repeated transitions.
  - Packing creates one shipment; dispatch requires bounded carrier/tracking values; delivery timestamps the shipment.
  - Seller-order transitions update the parent order's partial-shipment, shipment, and delivery state and append actor-attributed audit evidence.

- Status: PASS locally; deployed on Render with the API service.
- Implemented filtered work queues, approved-seller ownership enforcement, controlled picking/packing/dispatch/delivery, shipment tracking, aggregate order status updates, and audit evidence.
- Validation: full suite passes 23/23 tests; cross-seller access, invalid transitions, required dispatch data, shipment lifecycle, aggregate status, and audit persistence are covered.

### BE-008A Reseller catalogue and approval

- Acceptance criteria:
  - Active reseller accounts can manage their own store profile and prepare draft products while approval is pending.
  - Product creation and editing enforce unique SKU/store slug, positive ZAR pricing, active categories, public HTTPS image URLs, and accessible image descriptions.
  - Stock adjustments cannot reduce on-hand quantity below customer reservations and append inventory transaction evidence.
  - Cross-seller reads, edits, inventory changes, publication, and archival return no owned resource.
  - A system administrator can approve, reject, or suspend a reseller; approval activates the store.
  - Only approved resellers with active stores can publish products, and published products immediately satisfy the existing public-catalogue visibility rules.

- Status: PASS locally; awaiting Render release.
- Implemented pending-reseller workspace authorization, owned store/product CRUD, external-image metadata, inventory adjustments, soft archival, publish/unpublish controls, administrator decisions, and audit records.
- Added DATA-005 (`20260904064645_SeedMarketplaceCategories`) with six deterministic marketplace categories so a fresh deployment can accept reseller products.
- Validation: full API suite passes 27/27; release build has zero warnings/errors; EF reports no pending model changes.

## Known limitations and pending decisions

- Email delivery, confirmation links, password-reset delivery, and optional MFA depend on the notification work unit. They are not to be falsely represented as active until a sandbox notification adapter exists.
- The current Render Free PostgreSQL database expires and has no retained backups; it is not suitable for real users or production transactions.
- Direct product-image upload storage and the sandbox payment provider remain unselected implementation dependencies. Resellers can currently attach public HTTPS image URLs with required alt text.
- Database persistence protects key-ring availability, not key confidentiality by itself. Before a real production launch, wrap data-protection keys with an approved certificate or external key-encryption mechanism and verify restoration.

## Render release checkpoint

- Service: `mzansi-market-api` (`srv-da8lb75g1s2s739oncb0`), Docker, Frankfurt, Free.
- Public URL: `https://mzansi-market-api.onrender.com`.
- Source release: commit `efe85d2` on `main` (includes the clean Npgsql/GSS container runtime dependency).
- Environment: private `DATABASE_URL`, Production environment, controlled startup migrations, deployed-frontend-only CORS, authentication rate limiting, and a generated sandbox webhook secret.
- Database: DATA-002 through DATA-004 applied successfully at startup; Render logs report the database migrations are current.
- Platform health check: `/health/database`.
- Public verification: health 200/Healthy; customer registration 201; login and refresh issue opaque tokens; `/api/auth/me` returns the matching fictional customer and Customer role; invalid password returns 401; the customer frontend origin is allowed and an untrusted origin receives no CORS allow header.
- Free-tier limitation: cold starts can delay the first request after inactivity, and the database remains temporary development infrastructure.

## Next ready action

- Release BE-008A and DATA-005 to Render, then implement the remaining BE-008 staff category/promotion/role administration or resume BE-007 cancellations, returns, and refunds.

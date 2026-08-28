# DATA-001 — Initial marketplace schema

## Purpose and compatibility

Create the initial PostgreSQL schema for an empty development database. The change is additive: three schemas, application tables, foreign keys, checks, indexes, and six role seed rows. There is no existing application data to backfill.

The migration targets PostgreSQL 18 and EF Core 10. The generated SQL is idempotent and can be reviewed independently at `artifacts/database/InitialMarketplaceSchema.sql`.

## Deployment

1. Confirm the target is `mzansi-market-db` and that it is a non-production Render Free database.
2. Export its connection URL as `ConnectionStrings__DefaultConnection` without committing or pasting the secret into project files.
3. Run `dotnet tool restore`.
4. Run `dotnet ef database update --project src/MzansiMarket.Api --startup-project src/MzansiMarket.Api`.
5. Verify the EF migration history, schemas, table count, six role rows, constraints, and indexes.
6. Start the API and confirm `/health/database` is healthy.

## Failure and recovery

If deployment fails before EF records the migration, PostgreSQL rolls back the active migration transaction. On an empty development database, the migration can be rolled back with `dotnet ef database update 0` after inspecting the failure.

Render Free PostgreSQL has no retained backups and expires, so it is not a production target. Before real customer/seller data or payment activity, move to a paid database with recovery coverage and take a verified backup before every destructive migration.

## Validation evidence

- Release build and automated model tests pass.
- The idempotent script contains no data-dropping statements.
- Migration `20260827223811_InitialMarketplaceSchema` was applied to the Render PostgreSQL 18 development database.
- A repeat `database update` reported that the database was already current, confirming idempotent application.
- Live verification returned three application schemas, 33 application tables, one migration-history row, six seeded roles, 20 check constraints, 80 total application-schema indexes, and zero forbidden raw-card-data columns.
- External access was temporarily limited to one `/32` address for deployment and then removed. PostgreSQL inbound rules again block all public internet traffic.

## Deployment result

Status: complete on the Render Free development database `mzansi-market-db` in Frankfurt.

The 33 application tables exclude EF Core's `public.__EFMigrationsHistory` table. Including migration history, the deployment created 34 tables in total.

# DATA-002 - Persist authentication data-protection keys

## Purpose and compatibility

Add `identity.DataProtectionKeys` for ASP.NET Core's shared data-protection key ring. Identity bearer access and refresh tokens use this key ring; database persistence prevents every API restart or Render deployment from invalidating all sessions.

The migration is additive and contains one new table with no application-data backfill. It does not modify existing customer, seller, catalogue, order, payment, fulfilment, refund, payout, or audit rows.

## Deployment

1. Deploy the API configuration that calls `PersistKeysToDbContext<MarketplaceDbContext>()` together with migration `20260828054251_PersistDataProtectionKeys`.
2. Apply the migration to the non-production Render database before serving authentication traffic.
3. Start one API instance and perform a test login to create the first key when needed.
4. Verify the table exists, contains no user credentials, and remains available across an API restart.
5. Verify an access or refresh token issued before the restart remains valid afterward.

## Failure and recovery

If creation fails before EF records the migration, PostgreSQL rolls back the migration transaction. Fix the cause and re-run the migration. Do not roll back this table after real sessions are issued unless invalidating every active session is an explicit, approved security action.

## Current status

Generated and validated locally; not yet applied to the Render PostgreSQL database. Production authentication deployment remains blocked on applying this additive migration.

# DATA-003 - Customer invariants and checkout idempotency

## Purpose and compatibility

Add partial unique indexes that enforce at most one default address and one active cart per customer. Add nullable `Orders.CheckoutKey` and `Orders.PromotionCode` columns plus a filtered unique `(CustomerId, CheckoutKey)` index. Existing orders remain compatible because checkout keys are nullable; all API-created orders supply a key.

The change is split into migrations `20260828061026_EnforceCustomerDefaultsAndActiveCarts` and `20260828064649_AddCheckoutIdempotency`. Reviewable idempotent SQL is in `artifacts/database/EnforceCustomerDefaultsAndActiveCarts.sql` and `artifacts/database/AddCheckoutIdempotency.sql`.

## Pre-deployment checks

1. Verify no user currently has more than one `IsDefault = true` address.
2. Verify no customer currently has more than one `Status = 'Active'` cart.
3. Confirm no application writes checkout orders without a validated idempotency key after the API deployment.

## Failure and recovery

PostgreSQL applies each migration transactionally. If a uniqueness check fails, reconcile the duplicate development rows and rerun; do not delete customer or order history. The new order columns and index may be rolled back only while no deployed client depends on idempotent checkout replay.

## Current status

Generated and validated locally; not yet applied to the Render PostgreSQL database.

# DATA-004 - Sandbox payment event receipts

## Purpose and compatibility

Add nullable `PaymentRecords.PaymentKey` for customer-scoped initiation replay and add `PaymentProviderEvents` for duplicate-safe sandbox callbacks. The event table stores only provider identifiers, normalized event type, and receipt time; it does not retain callback payloads or payment-card data. The existing order lookup index is replaced by the left-prefixed `(OrderId, PaymentKey)` index.

Migration `20260828065853_AddSandboxPaymentEvents` and its idempotent SQL at `artifacts/database/AddSandboxPaymentEvents.sql` are additive for existing payment rows.

## Deployment and recovery

1. Configure `SandboxPayments__WebhookSecret` as a Render secret; never commit or log it.
2. Apply DATA-002 through DATA-004 before enabling API traffic.
3. Initiate one fictional sandbox payment and send a signed test outcome.
4. Verify a repeated event ID produces one receipt and one stock movement.

If migration application fails, PostgreSQL rolls it back transactionally. Once callback receipts exist, do not drop the event table during routine rollback because doing so removes replay evidence and can permit duplicate processing.

## Current status

Generated and validated locally; not yet applied to the Render PostgreSQL database.

# Mzansi Market Online

Seller-aware e-commerce marketplace backend foundation built with ASP.NET Core, EF Core, and PostgreSQL 18.

## Customer web application

The responsive React/TypeScript customer storefront lives in `src/MzansiMarket.CustomerWeb`.

```powershell
cd src/MzansiMarket.CustomerWeb
npm install
npm run dev
```

See the [customer-web README](src/MzansiMarket.CustomerWeb/README.md) for implemented interactions, quality checks, and the current mock-data boundary.

## Application API and identity

The ASP.NET Core API lives in `src/MzansiMarket.Api`. Its first implemented work unit provides:

- customer and seller registration;
- pending seller onboarding with a draft store;
- ASP.NET Core Identity password hashing and lockout;
- opaque bearer access and refresh tokens;
- active-account, approved-seller, staff, manager, and administrator authorization policies;
- PostgreSQL-backed data-protection keys so sessions survive API restarts;
- production CORS restricted to the deployed customer web origin; and
- per-client authentication rate limiting.

Authentication routes are rooted at `/api/auth`: `register/customer`, `register/seller`, `login`, `refresh`, `me`, `logout`, and `logout-all`. Access tokens expire after 15 minutes and refresh tokens after 14 days. The client must treat both as secrets. `logout` tells a client to discard its tokens; `logout-all` rotates the Identity security stamp and invalidates refresh sessions.

Email delivery, email confirmation, password-reset delivery, and optional MFA remain pending until the sandbox notification adapter is implemented.

Public catalogue routes provide active category and product discovery. Authenticated customers can manage their own South African address book and cart. `POST /api/checkout` requires an `Idempotency-Key` header and creates one platform order with a seller order per store, immutable address and product snapshots, server-calculated promotion/delivery/commission totals, and 15-minute stock reservations. Payment details are intentionally not accepted by this endpoint.

The sandbox payment adapter starts at `POST /api/orders/{orderId}/payments/sandbox` and accepts only `TestWallet` or `SandboxEft` method labels—never card details. Test provider outcomes are posted to `POST /api/payments/sandbox/events` with `X-Sandbox-Webhook-Secret`; configure the secret through `SandboxPayments__WebhookSecret`. Paid events atomically commit reserved stock and unlock fulfilment, while failed/cancelled events release stock. Event IDs and payment initiation keys are replay-safe.

Approved sellers and fulfilment staff use `/api/fulfilment/orders` for seller-isolated work queues. Controlled transitions enforce `ReadyForFulfilment → Picking → Packed → Shipped → Delivered`; dispatch requires carrier and tracking data, and each transition updates aggregate order state and appends audit evidence.

## Database foundation

The first migration creates separate `identity`, `marketplace`, and `audit` schemas. It supports customers, sellers and stores, product catalogues, stock control, multi-seller orders, sandbox payment references, fulfilment, returns, refunds, promotions, seller payouts, and audit records.

No raw payment-card number or security code is stored. Product and order money is represented as `numeric(18,2)` and uses `ZAR` by default.

## Local commands

```powershell
dotnet restore MzansiMarket.slnx
dotnet build MzansiMarket.slnx --configuration Release
dotnet test MzansiMarket.slnx --configuration Release
dotnet tool restore
dotnet ef database update --project src/MzansiMarket.Api --startup-project src/MzansiMarket.Api
```

Supply the database connection through `ConnectionStrings__DefaultConnection` or `DATABASE_URL`. Render-style `postgresql://` URLs are supported. Keep credentials outside source control.

The root `Dockerfile` publishes the API into the official .NET 10 ASP.NET runtime image and binds to Render's injected `PORT`. Set `Database__ApplyMigrations=true` only on the controlled API service to apply pending EF migrations before it accepts traffic; a migration failure stops startup.

## Design evidence

- [ERD](Project-Design-Portfolio/07-Database-Design/ERD.md)
- [Data dictionary](Project-Design-Portfolio/07-Database-Design/Data-Dictionary.md)
- [Migration plan](Project-Design-Portfolio/07-Database-Design/Migration-Plan.md)
- [Live database evidence](Project-Design-Portfolio/13-Project-Evidence/DATABASE-STATE.md)
- [Customer web state](Project-Design-Portfolio/13-Project-Evidence/CUSTOMER-WEB-STATE.md)
- [Backend implementation state](Project-Design-Portfolio/13-Project-Evidence/BACKEND-STATE.md)
- [Generated idempotent SQL](artifacts/database/InitialMarketplaceSchema.sql)
- [Authentication-key migration SQL](artifacts/database/PersistDataProtectionKeys.sql)
- [Customer-invariant migration SQL](artifacts/database/EnforceCustomerDefaultsAndActiveCarts.sql)
- [Checkout-idempotency migration SQL](artifacts/database/AddCheckoutIdempotency.sql)
- [Sandbox-payment migration SQL](artifacts/database/AddSandboxPaymentEvents.sql)

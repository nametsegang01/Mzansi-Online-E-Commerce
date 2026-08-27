# Mzansi Market Online

Seller-aware e-commerce marketplace backend foundation built with ASP.NET Core, EF Core, and PostgreSQL 18.

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

## Design evidence

- [ERD](Project-Design-Portfolio/07-Database-Design/ERD.md)
- [Data dictionary](Project-Design-Portfolio/07-Database-Design/Data-Dictionary.md)
- [Migration plan](Project-Design-Portfolio/07-Database-Design/Migration-Plan.md)
- [Live database evidence](Project-Design-Portfolio/13-Project-Evidence/DATABASE-STATE.md)
- [Generated idempotent SQL](artifacts/database/InitialMarketplaceSchema.sql)

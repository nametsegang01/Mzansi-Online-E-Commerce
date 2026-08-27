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

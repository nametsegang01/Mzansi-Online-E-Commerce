# Database implementation state

## Completed

- Render database: `mzansi-market-db`
- PostgreSQL version: 18
- Region and plan: Frankfurt, Free
- Applied migration: `20260827223811_InitialMarketplaceSchema`
- Application schemas: `identity`, `marketplace`, `audit`
- Application tables: 33, plus `public.__EFMigrationsHistory`
- Seeded roles: Customer, Seller, ProductAdministrator, FulfilmentEmployee, BusinessManager, SystemAdministrator
- Check constraints: 20
- Application-schema indexes: 80, including primary-key and unique-constraint indexes
- Forbidden PAN/CVV-style columns: 0
- Repeat migration result: already up to date
- Final public PostgreSQL access: blocked

## Verification performed

- Release build succeeded with zero warnings and errors.
- Five automated database-model and connection-normalization tests passed.
- NuGet vulnerability audit found no vulnerable direct or transitive packages.
- Generated idempotent SQL contains no `DROP`, `TRUNCATE`, or `DELETE` statements.
- Live queries verified schema counts, migration history, role seeds, constraints, indexes, and payment-data boundaries.

## Operational limitation

The Free database is suitable only for development/prototyping and is scheduled to expire in September 2026. It has no retained backup capability. Upgrade and establish tested backups before storing real customer, seller, order, or payment activity.

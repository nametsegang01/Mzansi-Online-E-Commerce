# Backend Follow-ups

- Add a first-login password-change flow for staff accounts. ASP.NET Core Identity has no built-in force-change flag; this requires an application-level implementation.
- TARGET has a migration REFERENCE does not (promotion constraints); do not share one database or merge migration histories without reconciling.
- The promotion-constraints migration has been generated but not applied to any database.
- The promotion migration's Down restores `"Value" > 0` and will fail if any 0% promotion exists.
- Before applying the promotion migration, check for percentage promotions above 100, unknown `Type` values, negative `MinimumOrderAmount`, and negative `UsageLimit` values. Do not apply until incompatible rows are resolved.
- Refund concurrency and rollback guarantees are untested against PostgreSQL: simultaneous decisions on one return, the per-order-item refund cap race, and rollback leaving no partial state. These tests must pass in CI or on a machine with Docker before merging to main.
- Sales reports reject mixed-currency orders; mixed-currency reporting remains unsupported.
- Customer return creation is not implemented. Nothing in this API moves a return from Approved to Received/Refunded or settles a Pending RefundRecord.
- A first-login password-change flow for staff accounts remains open. ASP.NET Core Identity has no built-in force-change flag; this requires an application-level implementation.
- The SSH.NET 2024.2.0 NU1903 high-severity warning is transitive through the test-only Testcontainers.PostgreSql -> Testcontainers dependency; leave it unchanged in this branch.
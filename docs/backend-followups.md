# Backend Follow-ups

- Add a first-login password-change flow for staff accounts. ASP.NET Core Identity has no built-in force-change flag; this requires an application-level implementation.
- TARGET has a migration REFERENCE does not (promotion constraints); do not share one database or merge migration histories without reconciling.
- The promotion-constraints migration has been generated but not applied to any database.
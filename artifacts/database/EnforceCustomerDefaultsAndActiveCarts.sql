START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260828061026_EnforceCustomerDefaultsAndActiveCarts') THEN
    CREATE UNIQUE INDEX "IX_Carts_CustomerId" ON marketplace."Carts" ("CustomerId") WHERE "Status" = 'Active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260828061026_EnforceCustomerDefaultsAndActiveCarts') THEN
    CREATE UNIQUE INDEX "IX_Addresses_UserId" ON marketplace."Addresses" ("UserId") WHERE "IsDefault";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260828061026_EnforceCustomerDefaultsAndActiveCarts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260828061026_EnforceCustomerDefaultsAndActiveCarts', '10.0.11');
    END IF;
END $EF$;
COMMIT;


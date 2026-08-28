START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260828064649_AddCheckoutIdempotency') THEN
    ALTER TABLE marketplace."Orders" ADD "CheckoutKey" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260828064649_AddCheckoutIdempotency') THEN
    ALTER TABLE marketplace."Orders" ADD "PromotionCode" character varying(80);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260828064649_AddCheckoutIdempotency') THEN
    CREATE UNIQUE INDEX "IX_Orders_CustomerId_CheckoutKey" ON marketplace."Orders" ("CustomerId", "CheckoutKey") WHERE "CheckoutKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260828064649_AddCheckoutIdempotency') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260828064649_AddCheckoutIdempotency', '10.0.11');
    END IF;
END $EF$;
COMMIT;


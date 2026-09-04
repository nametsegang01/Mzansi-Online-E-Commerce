START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904064645_SeedMarketplaceCategories') THEN
    INSERT INTO marketplace."Categories" ("Id", "CreatedAt", "IsActive", "Name", "ParentCategoryId", "Slug", "UpdatedAt")
    VALUES ('71111111-1111-1111-1111-111111111111', TIMESTAMPTZ '2026-08-28T00:00:00+00:00', TRUE, 'Home & living', NULL, 'home-living', TIMESTAMPTZ '2026-08-28T00:00:00+00:00');
    INSERT INTO marketplace."Categories" ("Id", "CreatedAt", "IsActive", "Name", "ParentCategoryId", "Slug", "UpdatedAt")
    VALUES ('72222222-2222-2222-2222-222222222222', TIMESTAMPTZ '2026-08-28T00:00:00+00:00', TRUE, 'Fashion', NULL, 'fashion', TIMESTAMPTZ '2026-08-28T00:00:00+00:00');
    INSERT INTO marketplace."Categories" ("Id", "CreatedAt", "IsActive", "Name", "ParentCategoryId", "Slug", "UpdatedAt")
    VALUES ('73333333-3333-3333-3333-333333333333', TIMESTAMPTZ '2026-08-28T00:00:00+00:00', TRUE, 'Beauty', NULL, 'beauty', TIMESTAMPTZ '2026-08-28T00:00:00+00:00');
    INSERT INTO marketplace."Categories" ("Id", "CreatedAt", "IsActive", "Name", "ParentCategoryId", "Slug", "UpdatedAt")
    VALUES ('74444444-4444-4444-4444-444444444444', TIMESTAMPTZ '2026-08-28T00:00:00+00:00', TRUE, 'Food & pantry', NULL, 'food-pantry', TIMESTAMPTZ '2026-08-28T00:00:00+00:00');
    INSERT INTO marketplace."Categories" ("Id", "CreatedAt", "IsActive", "Name", "ParentCategoryId", "Slug", "UpdatedAt")
    VALUES ('75555555-5555-5555-5555-555555555555', TIMESTAMPTZ '2026-08-28T00:00:00+00:00', TRUE, 'Art & craft', NULL, 'art-craft', TIMESTAMPTZ '2026-08-28T00:00:00+00:00');
    INSERT INTO marketplace."Categories" ("Id", "CreatedAt", "IsActive", "Name", "ParentCategoryId", "Slug", "UpdatedAt")
    VALUES ('76666666-6666-6666-6666-666666666666', TIMESTAMPTZ '2026-08-28T00:00:00+00:00', TRUE, 'Electronics', NULL, 'electronics', TIMESTAMPTZ '2026-08-28T00:00:00+00:00');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904064645_SeedMarketplaceCategories') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260904064645_SeedMarketplaceCategories', '10.0.11');
    END IF;
END $EF$;
COMMIT;


-- Migración AddNotificationDedupKey (recordatorios: pago de tarjeta, estado nuevo,
-- presupuesto, cuenta desactualizada y resumen semanal). Idempotente.
-- Requiere que AddUserConsents ya esté aplicada (scripts/sql/add_user_consents.sql).

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005221319_AddNotificationDedupKey') THEN
    ALTER TABLE notifications ADD "DedupKey" character varying(160);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005221319_AddNotificationDedupKey') THEN
    CREATE UNIQUE INDEX "IX_notifications_UserId_DedupKey" ON notifications ("UserId", "DedupKey") WHERE "DedupKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005221319_AddNotificationDedupKey') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261005221319_AddNotificationDedupKey', '10.0.0');
    END IF;
END $EF$;
COMMIT;

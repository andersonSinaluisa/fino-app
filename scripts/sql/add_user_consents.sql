-- Migración AddUserConsents (aceptación de Términos/Privacidad, 18+ y datos de uso). Idempotente.
-- Requiere que AddCreditCards ya esté aplicada (scripts/sql/add_credit_cards.sql).
-- Los usuarios existentes no tienen filas: la app les pedirá aceptar al abrirla.

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002205743_AddUserConsents') THEN
    CREATE TABLE user_consents (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Kind" character varying(24) NOT NULL,
        "Version" character varying(32) NOT NULL,
        "Granted" boolean NOT NULL,
        "Source" character varying(24) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_user_consents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_user_consents_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002205743_AddUserConsents') THEN
    CREATE INDEX "IX_user_consents_UserId_Kind_CreatedAt" ON user_consents ("UserId", "Kind", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002205743_AddUserConsents') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261002205743_AddUserConsents', '10.0.0');
    END IF;
END $EF$;
COMMIT;

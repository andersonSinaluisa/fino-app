-- Migración AddBudgets (Presupuestos). Idempotente: si ya se aplicó, no hace nada.
-- Generado con: dotnet ef migrations script 20260913233841_ProductionSchemaSync 20260930015136_AddBudgets --idempotent

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930015136_AddBudgets') THEN
    CREATE TABLE budgets (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Name" character varying(60) NOT NULL,
        "CategoryId" uuid,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "Period" character varying(16) NOT NULL,
        "StartDate" date NOT NULL,
        "EndDate" date,
        "IsRecurring" boolean NOT NULL,
        "ReserveFunds" boolean NOT NULL,
        "Priority" character varying(16) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_budgets" PRIMARY KEY ("Id"),
        CONSTRAINT ck_budgets_amount_positive CHECK ("Amount" > 0),
        CONSTRAINT ck_budgets_end_after_start CHECK ("EndDate" IS NULL OR "EndDate" >= "StartDate"),
        CONSTRAINT "FK_budgets_categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES categories ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_budgets_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930015136_AddBudgets') THEN
    CREATE TABLE budget_amount_revisions (
        "Id" uuid NOT NULL,
        "BudgetId" uuid NOT NULL,
        "EffectiveFrom" date NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_budget_amount_revisions" PRIMARY KEY ("Id"),
        CONSTRAINT ck_budget_amount_revisions_amount_positive CHECK ("Amount" > 0),
        CONSTRAINT "FK_budget_amount_revisions_budgets_BudgetId" FOREIGN KEY ("BudgetId") REFERENCES budgets ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930015136_AddBudgets') THEN
    CREATE UNIQUE INDEX "IX_budget_amount_revisions_BudgetId_EffectiveFrom" ON budget_amount_revisions ("BudgetId", "EffectiveFrom");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930015136_AddBudgets') THEN
    CREATE INDEX "IX_budgets_CategoryId" ON budgets ("CategoryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930015136_AddBudgets') THEN
    CREATE INDEX "IX_budgets_UserId_CategoryId_IsActive_StartDate_EndDate" ON budgets ("UserId", "CategoryId", "IsActive", "StartDate", "EndDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930015136_AddBudgets') THEN
    CREATE INDEX "IX_budgets_UserId_IsActive" ON budgets ("UserId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260930015136_AddBudgets') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260930015136_AddBudgets', '10.0.12');
    END IF;
END $EF$;
COMMIT;


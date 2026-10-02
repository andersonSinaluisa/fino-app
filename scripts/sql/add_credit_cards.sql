-- Migración AddCreditCards (Tarjetas de crédito). Idempotente: si ya se aplicó, no hace nada.
-- Requiere AddBudgets y AddTransactionSplits (scripts/sql/add_budgets.sql, scripts/sql/add_transaction_splits.sql).
-- También clasifica los movimientos de cuentas de tipo tarjeta que ya existían (compra/pago/devolución...).

﻿START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    ALTER TABLE transactions ADD "CardMovementType" character varying(16);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    ALTER TABLE imports ADD "CardClosingDate" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    ALTER TABLE imports ADD "CardCreditLimit" numeric(18,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    ALTER TABLE imports ADD "CardDueDate" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    ALTER TABLE imports ADD "CardMinimumPayment" numeric(18,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    ALTER TABLE imports ADD "CardPeriodStart" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    ALTER TABLE imports ADD "CardStatementBalance" numeric(18,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    ALTER TABLE import_rows ADD "SkipReason" character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE TABLE credit_cards (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "FinancialAccountId" uuid NOT NULL,
        "Network" character varying(24) NOT NULL,
        "CreditLimit" numeric(18,2) NOT NULL,
        "ClosingDay" integer NOT NULL,
        "PaymentDueDay" integer NOT NULL,
        "AutoReserve" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_credit_cards" PRIMARY KEY ("Id"),
        CONSTRAINT ck_credit_cards_closing_day CHECK ("ClosingDay" BETWEEN 1 AND 31),
        CONSTRAINT ck_credit_cards_due_day CHECK ("PaymentDueDay" BETWEEN 1 AND 31),
        CONSTRAINT ck_credit_cards_limit_positive CHECK ("CreditLimit" > 0),
        CONSTRAINT "FK_credit_cards_financial_accounts_FinancialAccountId" FOREIGN KEY ("FinancialAccountId") REFERENCES financial_accounts ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE TABLE credit_card_statements (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "CreditCardId" uuid NOT NULL,
        "PeriodStart" date,
        "ClosingDate" date NOT NULL,
        "DueDate" date NOT NULL,
        "StatementBalance" numeric(18,2) NOT NULL,
        "MinimumPayment" numeric(18,2),
        "Source" character varying(16) NOT NULL,
        "ImportId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_credit_card_statements" PRIMARY KEY ("Id"),
        CONSTRAINT ck_credit_card_statements_balance CHECK ("StatementBalance" >= 0),
        CONSTRAINT ck_credit_card_statements_due_after_closing CHECK ("DueDate" > "ClosingDate"),
        CONSTRAINT "FK_credit_card_statements_credit_cards_CreditCardId" FOREIGN KEY ("CreditCardId") REFERENCES credit_cards ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE TABLE installment_plans (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "CreditCardId" uuid NOT NULL,
        "TransactionId" uuid NOT NULL,
        "OriginalAmount" numeric(18,2) NOT NULL,
        "NumberOfInstallments" integer NOT NULL,
        "InstallmentAmount" numeric(18,2) NOT NULL,
        "InterestRate" numeric(5,2),
        "StartDate" date NOT NULL,
        "Status" character varying(16) NOT NULL,
        "CancelledOn" date,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_installment_plans" PRIMARY KEY ("Id"),
        CONSTRAINT ck_installment_plans_amount_positive CHECK ("OriginalAmount" > 0),
        CONSTRAINT ck_installment_plans_count CHECK ("NumberOfInstallments" BETWEEN 2 AND 72),
        CONSTRAINT "FK_installment_plans_credit_cards_CreditCardId" FOREIGN KEY ("CreditCardId") REFERENCES credit_cards ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_installment_plans_transactions_TransactionId" FOREIGN KEY ("TransactionId") REFERENCES transactions ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE TABLE installments (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "InstallmentPlanId" uuid NOT NULL,
        "Number" integer NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "ClosingDate" date NOT NULL,
        "DueDate" date NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_installments" PRIMARY KEY ("Id"),
        CONSTRAINT ck_installments_amount_positive CHECK ("Amount" > 0),
        CONSTRAINT "FK_installments_installment_plans_InstallmentPlanId" FOREIGN KEY ("InstallmentPlanId") REFERENCES installment_plans ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE UNIQUE INDEX "IX_credit_card_statements_CreditCardId_ClosingDate" ON credit_card_statements ("CreditCardId", "ClosingDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE INDEX "IX_credit_card_statements_UserId" ON credit_card_statements ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE UNIQUE INDEX "IX_credit_cards_FinancialAccountId" ON credit_cards ("FinancialAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE INDEX "IX_credit_cards_UserId" ON credit_cards ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE INDEX "IX_installment_plans_CreditCardId_Status" ON installment_plans ("CreditCardId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE UNIQUE INDEX "IX_installment_plans_TransactionId" ON installment_plans ("TransactionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE INDEX "IX_installment_plans_UserId" ON installment_plans ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE UNIQUE INDEX "IX_installments_InstallmentPlanId_Number" ON installments ("InstallmentPlanId", "Number");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    CREATE INDEX "IX_installments_UserId_ClosingDate" ON installments ("UserId", "ClosingDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    UPDATE transactions t SET "CardMovementType" = CASE
        WHEN t."Direction" = 'Expense' AND UPPER(t."Description") LIKE '%INTERES%' THEN 'Interest'
        WHEN t."Direction" = 'Expense' AND (UPPER(t."Description") LIKE '%COMISION%' OR UPPER(t."Description") LIKE '%IMPUESTO%' OR UPPER(t."Description") LIKE '%MEMBRESIA%') THEN 'Fee'
        WHEN t."Direction" = 'Expense' AND (UPPER(t."Description") LIKE '%AVANCE%' OR UPPER(t."Description") LIKE '%RETIRO%') THEN 'CashAdvance'
        WHEN t."Direction" = 'Expense' THEN 'Purchase'
        WHEN UPPER(t."Description") LIKE '%DEVOLUCION%' OR UPPER(t."Description") LIKE '%REVERSO%' OR UPPER(t."Description") LIKE '%REEMBOLSO%' OR UPPER(t."Description") LIKE '%ANULACION%' THEN 'Refund'
        WHEN UPPER(t."Description") LIKE '%PAGO%' OR UPPER(t."Description") LIKE '%ABONO%' OR UPPER(t."Description") LIKE '%GRACIAS%' THEN 'Payment'
        ELSE 'Refund'
    END
    FROM financial_accounts a
    WHERE a."Id" = t."FinancialAccountId"
      AND a."AccountType" = 'CreditCard'
      AND t."CardMovementType" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    UPDATE transactions SET "IsInternalTransfer" = TRUE
    WHERE "CardMovementType" IN ('Payment', 'CashAdvance', 'Adjustment')
      AND "IsInternalTransfer" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002154619_AddCreditCards') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261002154619_AddCreditCards', '10.0.12');
    END IF;
END $EF$;
COMMIT;


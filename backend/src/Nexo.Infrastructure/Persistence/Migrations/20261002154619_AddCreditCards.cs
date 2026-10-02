using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardMovementType",
                table: "transactions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CardClosingDate",
                table: "imports",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CardCreditLimit",
                table: "imports",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CardDueDate",
                table: "imports",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CardMinimumPayment",
                table: "imports",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CardPeriodStart",
                table: "imports",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CardStatementBalance",
                table: "imports",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkipReason",
                table: "import_rows",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credit_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Network = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreditLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingDay = table.Column<int>(type: "integer", nullable: false),
                    PaymentDueDay = table.Column<int>(type: "integer", nullable: false),
                    AutoReserve = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_cards", x => x.Id);
                    table.CheckConstraint("ck_credit_cards_closing_day", "\"ClosingDay\" BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_credit_cards_due_day", "\"PaymentDueDay\" BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_credit_cards_limit_positive", "\"CreditLimit\" > 0");
                    table.ForeignKey(
                        name: "FK_credit_cards_financial_accounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "financial_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_card_statements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ClosingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StatementBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MinimumPayment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_card_statements", x => x.Id);
                    table.CheckConstraint("ck_credit_card_statements_balance", "\"StatementBalance\" >= 0");
                    table.CheckConstraint("ck_credit_card_statements_due_after_closing", "\"DueDate\" > \"ClosingDate\"");
                    table.ForeignKey(
                        name: "FK_credit_card_statements_credit_cards_CreditCardId",
                        column: x => x.CreditCardId,
                        principalTable: "credit_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "installment_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NumberOfInstallments = table.Column<int>(type: "integer", nullable: false),
                    InstallmentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CancelledOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installment_plans", x => x.Id);
                    table.CheckConstraint("ck_installment_plans_amount_positive", "\"OriginalAmount\" > 0");
                    table.CheckConstraint("ck_installment_plans_count", "\"NumberOfInstallments\" BETWEEN 2 AND 72");
                    table.ForeignKey(
                        name: "FK_installment_plans_credit_cards_CreditCardId",
                        column: x => x.CreditCardId,
                        principalTable: "credit_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_installment_plans_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "installments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallmentPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installments", x => x.Id);
                    table.CheckConstraint("ck_installments_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_installments_installment_plans_InstallmentPlanId",
                        column: x => x.InstallmentPlanId,
                        principalTable: "installment_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_card_statements_CreditCardId_ClosingDate",
                table: "credit_card_statements",
                columns: new[] { "CreditCardId", "ClosingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_card_statements_UserId",
                table: "credit_card_statements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_cards_FinancialAccountId",
                table: "credit_cards",
                column: "FinancialAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_cards_UserId",
                table: "credit_cards",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_installment_plans_CreditCardId_Status",
                table: "installment_plans",
                columns: new[] { "CreditCardId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_installment_plans_TransactionId",
                table: "installment_plans",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installment_plans_UserId",
                table: "installment_plans",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_installments_InstallmentPlanId_Number",
                table: "installments",
                columns: new[] { "InstallmentPlanId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installments_UserId_ClosingDate",
                table: "installments",
                columns: new[] { "UserId", "ClosingDate" });

            // Tarjetas de crédito: cuentas de tipo tarjeta creadas antes de este
            // módulo. Sus movimientos ya existentes reciben su tipo con la misma
            // regla que CreditCardMovementRules.Classify, y los pagos pasan a ser
            // neutros: hasta hoy un "PAGO TARJETA" importado en la tarjeta contaba
            // como ingreso (y su contraparte del banco como gasto). Solo Postgres:
            // en la base SQLite de pruebas, recién creada, no hay filas que tocar.
            if (migrationBuilder.IsPostgres())
            {
                migrationBuilder.Sql("""
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
                    """);

                migrationBuilder.Sql("""
                    UPDATE transactions SET "IsInternalTransfer" = TRUE
                    WHERE "CardMovementType" IN ('Payment', 'CashAdvance', 'Adjustment')
                      AND "IsInternalTransfer" = FALSE;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_card_statements");

            migrationBuilder.DropTable(
                name: "installments");

            migrationBuilder.DropTable(
                name: "installment_plans");

            migrationBuilder.DropTable(
                name: "credit_cards");

            migrationBuilder.DropColumn(
                name: "CardMovementType",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "CardClosingDate",
                table: "imports");

            migrationBuilder.DropColumn(
                name: "CardCreditLimit",
                table: "imports");

            migrationBuilder.DropColumn(
                name: "CardDueDate",
                table: "imports");

            migrationBuilder.DropColumn(
                name: "CardMinimumPayment",
                table: "imports");

            migrationBuilder.DropColumn(
                name: "CardPeriodStart",
                table: "imports");

            migrationBuilder.DropColumn(
                name: "CardStatementBalance",
                table: "imports");

            migrationBuilder.DropColumn(
                name: "SkipReason",
                table: "import_rows");
        }
    }
}

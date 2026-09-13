using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileTransactionCategorizationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Solo PostgreSQL: este parche es idempotente a propósito para bases ya
            // desplegadas, y su sintaxis (IF NOT EXISTS en ADD COLUMN) no existe en
            // SQLite, donde InitialCreate ya dejó el esquema al día.
            if (!migrationBuilder.IsPostgres())
            {
                return;
            }

            migrationBuilder.Sql(
                """
                ALTER TABLE "transactions"
                ADD COLUMN IF NOT EXISTS "CategorySource" character varying(16) NOT NULL DEFAULT 'Uncategorized';

                ALTER TABLE "transactions"
                ALTER COLUMN "CategorySource" DROP DEFAULT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "transactions"
                ADD COLUMN IF NOT EXISTS "CategorizationRuleId" uuid NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_transactions_UserId_NormalizedDescription"
                ON "transactions" ("UserId", "NormalizedDescription");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Solo PostgreSQL: este parche es idempotente a propósito para bases ya
            // desplegadas, y su sintaxis (IF NOT EXISTS en ADD COLUMN) no existe en
            // SQLite, donde InitialCreate ya dejó el esquema al día.
            if (!migrationBuilder.IsPostgres())
            {
                return;
            }

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_transactions_UserId_NormalizedDescription";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "transactions"
                DROP COLUMN IF EXISTS "CategorizationRuleId";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "transactions"
                DROP COLUMN IF EXISTS "CategorySource";
                """);
        }
    }
}

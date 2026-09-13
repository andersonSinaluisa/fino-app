using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileImportRowCategorizationSchema : Migration
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
                ALTER TABLE "import_rows"
                ADD COLUMN IF NOT EXISTS "SuggestedCategorySource" character varying(16) NOT NULL DEFAULT 'Uncategorized';

                ALTER TABLE "import_rows"
                ALTER COLUMN "SuggestedCategorySource" DROP DEFAULT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "import_rows"
                ADD COLUMN IF NOT EXISTS "SuggestedCategorizationRuleId" uuid NULL;
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
                ALTER TABLE "import_rows"
                DROP COLUMN IF EXISTS "SuggestedCategorizationRuleId";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "import_rows"
                DROP COLUMN IF EXISTS "SuggestedCategorySource";
                """);
        }
    }
}

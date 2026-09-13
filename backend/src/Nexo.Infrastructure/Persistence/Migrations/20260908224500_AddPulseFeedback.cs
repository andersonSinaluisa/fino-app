using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPulseFeedback : Migration
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
                ALTER TABLE "financial_pulses"
                ADD COLUMN IF NOT EXISTS "FeedbackHelpful" boolean NULL;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "financial_pulses"
                ADD COLUMN IF NOT EXISTS "FeedbackAt" timestamp with time zone NULL;
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
                ALTER TABLE "financial_pulses"
                DROP COLUMN IF EXISTS "FeedbackAt";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "financial_pulses"
                DROP COLUMN IF EXISTS "FeedbackHelpful";
                """);
        }
    }
}

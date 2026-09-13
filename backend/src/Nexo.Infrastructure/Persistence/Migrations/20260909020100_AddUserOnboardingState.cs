using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserOnboardingState : Migration
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
                ALTER TABLE "users"
                ADD COLUMN IF NOT EXISTS "OnboardingStartedAt" timestamp with time zone NULL;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "users"
                ADD COLUMN IF NOT EXISTS "OnboardingTutorialCompletedAt" timestamp with time zone NULL;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "users"
                ADD COLUMN IF NOT EXISTS "OnboardingSkippedAt" timestamp with time zone NULL;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "users"
                ADD COLUMN IF NOT EXISTS "FirstAccountAddedAt" timestamp with time zone NULL;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "users"
                ADD COLUMN IF NOT EXISTS "FirstImportCompletedAt" timestamp with time zone NULL;
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
                ALTER TABLE "users"
                DROP COLUMN IF EXISTS "FirstImportCompletedAt";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "users"
                DROP COLUMN IF EXISTS "FirstAccountAddedAt";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "users"
                DROP COLUMN IF EXISTS "OnboardingSkippedAt";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "users"
                DROP COLUMN IF EXISTS "OnboardingTutorialCompletedAt";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "users"
                DROP COLUMN IF EXISTS "OnboardingStartedAt";
                """);
        }
    }
}

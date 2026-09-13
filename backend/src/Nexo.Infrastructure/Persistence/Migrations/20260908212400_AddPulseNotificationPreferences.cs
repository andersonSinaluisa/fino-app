using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPulseNotificationPreferences : Migration
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
                ALTER TABLE "devices"
                ADD COLUMN IF NOT EXISTS "NotifyOnPulses" boolean NOT NULL DEFAULT TRUE;

                ALTER TABLE "devices"
                ALTER COLUMN "NotifyOnPulses" DROP DEFAULT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "devices"
                ADD COLUMN IF NOT EXISTS "QuietHoursStartHour" integer NULL;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "devices"
                ADD COLUMN IF NOT EXISTS "QuietHoursEndHour" integer NULL;
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
                ALTER TABLE "devices"
                DROP COLUMN IF EXISTS "QuietHoursEndHour";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "devices"
                DROP COLUMN IF EXISTS "QuietHoursStartHour";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "devices"
                DROP COLUMN IF EXISTS "NotifyOnPulses";
                """);
        }
    }
}

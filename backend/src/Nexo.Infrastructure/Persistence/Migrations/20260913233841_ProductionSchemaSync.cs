using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionSchemaSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsPostgres())
            {
                return;
            }

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnPulses",
                table: "devices",
                type: null,
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "QuietHoursStartHour",
                table: "devices",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuietHoursEndHour",
                table: "devices",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FeedbackAt",
                table: "financial_pulses",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FeedbackHelpful",
                table: "financial_pulses",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingStartedAt",
                table: "users",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingTutorialCompletedAt",
                table: "users",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingSkippedAt",
                table: "users",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstAccountAddedAt",
                table: "users",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstImportCompletedAt",
                table: "users",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedProviderCode",
                table: "imports",
                type: null,
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsPostgres())
            {
                return;
            }

            migrationBuilder.DropColumn(name: "DetectedProviderCode", table: "imports");

            migrationBuilder.DropColumn(name: "FirstImportCompletedAt", table: "users");
            migrationBuilder.DropColumn(name: "FirstAccountAddedAt", table: "users");
            migrationBuilder.DropColumn(name: "OnboardingSkippedAt", table: "users");
            migrationBuilder.DropColumn(name: "OnboardingTutorialCompletedAt", table: "users");
            migrationBuilder.DropColumn(name: "OnboardingStartedAt", table: "users");

            migrationBuilder.DropColumn(name: "FeedbackHelpful", table: "financial_pulses");
            migrationBuilder.DropColumn(name: "FeedbackAt", table: "financial_pulses");

            migrationBuilder.DropColumn(name: "QuietHoursEndHour", table: "devices");
            migrationBuilder.DropColumn(name: "QuietHoursStartHour", table: "devices");
            migrationBuilder.DropColumn(name: "NotifyOnPulses", table: "devices");

        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Movimientos divididos. Additive only: two columns with safe defaults on
    /// `transactions` (IsSplit = false, SplitVersion = 0 -- every existing movement
    /// keeps behaving exactly as before) and the new `transaction_splits` table.
    /// No existing row is rewritten. Deleting a movement cascades to its splits;
    /// deleting a category still used by a split is refused (NO ACTION).
    /// </summary>
    public partial class AddTransactionSplits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSplit",
                table: "transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SplitVersion",
                table: "transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "transaction_splits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_splits", x => x.Id);
                    table.CheckConstraint("ck_transaction_splits_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_transaction_splits_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_transaction_splits_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_splits_CategoryId",
                table: "transaction_splits",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_splits_TransactionId",
                table: "transaction_splits",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_splits_TransactionId_CategoryId",
                table: "transaction_splits",
                columns: new[] { "TransactionId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_splits_UserId_CategoryId",
                table: "transaction_splits",
                columns: new[] { "UserId", "CategoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transaction_splits");

            migrationBuilder.DropColumn(
                name: "IsSplit",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SplitVersion",
                table: "transactions");
        }
    }
}

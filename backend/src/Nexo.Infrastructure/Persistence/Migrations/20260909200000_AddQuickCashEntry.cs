using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Registro rápido de efectivo. Un único cambio de esquema: la columna que hace
    /// idempotente el guardado (§36, "una misma acción no debe crear dos
    /// movimientos").
    ///
    /// Todo lo demás que el registro rápido necesita ya cabía en el modelo actual, y
    /// eso era justamente el objetivo: la cuenta de efectivo es un
    /// <c>FinancialAccount</c> normal con proveedor EFECTIVO (dato semilla, no
    /// esquema), y el movimiento en efectivo es un <c>Transaction</c> normal con
    /// <c>Source = Manual</c>, un valor del enum que existía desde el primer día y
    /// que hasta ahora ningún camino del backend producía.
    ///
    /// A diferencia de las migraciones "Reconcile" de este repositorio, esta usa las
    /// operaciones de EF y no SQL crudo: la columna es nueva de verdad, así que
    /// también tiene que crearse en SQLite, donde corre la suite de integración. SQL
    /// específico de Postgres aquí dejaría la columna sin crear en los tests.
    /// </summary>
    /// <inheritdoc />
    public partial class AddQuickCashEntry : Migration
    {
        private const string IndexName = "IX_transactions_UserId_ClientRequestId";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "transactions",
                type: migrationBuilder.IsPostgres() ? "uuid" : null,
                nullable: true);

            // ÚNICO y PARCIAL. Único porque es la garantía real contra el doble
            // toque: la comprobación previa en QuickTransactionService resuelve el
            // caso normal, pero solo la base de datos puede arbitrar dos peticiones
            // que llegan a la vez. Parcial porque casi todas las filas (todo lo
            // importado, todo lo detectado por correo) tienen NULL aquí y no tienen
            // por qué ocupar espacio en el índice. Postgres y SQLite soportan
            // índices parciales, así que esto vale en los dos.
            migrationBuilder.CreateIndex(
                name: IndexName,
                table: "transactions",
                columns: ["UserId", "ClientRequestId"],
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: IndexName, table: "transactions");

            migrationBuilder.DropColumn(name: "ClientRequestId", table: "transactions");
        }
    }
}

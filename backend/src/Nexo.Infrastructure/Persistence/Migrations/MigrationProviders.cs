using Microsoft.EntityFrameworkCore.Migrations;

namespace Nexo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Estas migraciones se ejecutan contra DOS proveedores: PostgreSQL en todos los
    /// entornos reales, y SQLite en la suite de integración (ver NexoApiFactory: la
    /// suite arranca la API de verdad contra una base SQLite desechable, sin Docker).
    ///
    /// La diferencia importa porque varias migraciones de este repositorio son
    /// "reconciliadoras": existen para poner al día una base de Postgres ya
    /// desplegada, y por eso usan SQL idempotente (<c>ADD COLUMN IF NOT EXISTS</c>).
    /// Sobre una base recién creada por InitialCreate no cambian nada -- pero SQLite
    /// NO entiende <c>IF NOT EXISTS</c> en <c>ADD COLUMN</c> y aborta con un error de
    /// sintaxis, tumbando toda la suite antes del primer test.
    ///
    /// De ahí este ayudante: cada migración de reconciliación pregunta primero si de
    /// verdad está hablando con Postgres. En SQLite se salta el parche, que es lo
    /// correcto: allí la columna ya existe porque InitialCreate acaba de crearla.
    /// </summary>
    internal static class MigrationProviders
    {
        public static bool IsPostgres(this MigrationBuilder migrationBuilder) =>
            migrationBuilder.ActiveProvider?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    }
}

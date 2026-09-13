using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Accounts;
using Nexo.Domain.Common;
using Nexo.Domain.Providers;

namespace Nexo.Application.QuickEntry;

/// <summary>
/// Registro rápido de efectivo (§23): "si todavía no existe una cuenta Efectivo,
/// revisar arquitectura y crearla correctamente".
///
/// Es un <c>get-or-create</c> idempotente y perezoso: la cuenta aparece la primera
/// vez que la persona registra efectivo, no al registrarse en Fino. Así nadie
/// termina con una cuenta "Efectivo · $0" en Home solo por haber abierto la app.
///
/// La cuenta que crea no tiene nada especial: es un <see cref="FinancialAccount"/>
/// normal con proveedor EFECTIVO, de modo que Home, Movimientos, Estadísticas y el
/// cálculo de saldo la tratan exactamente igual que a un banco, sin una sola rama
/// <c>if (esEfectivo)</c> en ningún sitio.
/// </summary>
public interface ICashAccountProvisioner
{
    /// <summary>
    /// Devuelve la cuenta de efectivo de la persona, creándola si hace falta. El
    /// llamador es quien hace <c>SaveChanges</c>: así crear la cuenta y registrar
    /// el primer movimiento son una sola transacción de base de datos y es
    /// imposible quedarse con la cuenta creada pero sin el movimiento que la
    /// motivó.
    /// </summary>
    Task<FinancialAccount> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>La cuenta de efectivo si ya existe, sin crear nada.</summary>
    Task<FinancialAccount?> FindAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class CashAccountProvisioner(INexoDbContext db, IClock clock) : ICashAccountProvisioner
{
    public const string DefaultAlias = "Efectivo";

    public Task<FinancialAccount?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.FinancialAccounts
            .Where(a => a.UserId == userId && a.ProviderCode == ProviderCodes.Cash)
            // Si por cualquier razón hubiera más de una (un import antiguo, una
            // migración de datos), se usa siempre la más antigua: es la que tiene
            // el historial detrás. Elegir "la primera que devuelva Postgres" haría
            // que el saldo de efectivo cambiara entre peticiones.
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<FinancialAccount> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(userId, cancellationToken);
        if (existing is not null)
        {
            if (existing.IsArchived)
            {
                // Registrar efectivo cuando la cuenta está archivada significa que la
                // persona volvió a usarla. Crear una segunda cuenta partiría el saldo
                // en dos; lo correcto es reabrir la que ya tiene su historial.
                existing.Unarchive(clock.UtcNow);
            }

            return existing;
        }

        var provider = await db.Providers
            .FirstOrDefaultAsync(p => p.Code == ProviderCodes.Cash, cancellationToken)
            ?? throw new NotFoundException("Provider", ProviderCodes.Cash);

        var account = FinancialAccount.Open(
            userId,
            provider,
            DefaultAlias,
            AccountType.Cash,
            ConnectionMode.Manual,
            clock.UtcNow,
            // Sin máscara (el efectivo no tiene número de cuenta) y sin saldo
            // inicial: §24 dice explícitamente que no declarar cuánto efectivo se
            // tiene NO debe bloquear el registro de movimientos. El saldo arranca
            // en 0 y estimado, y la persona lo ancla cuando quiera.
            mask: null,
            currency: Currency.Usd,
            openingVerifiedBalance: null,
            // Primero en la lista: es la cuenta que más veces al día se toca.
            displayOrder: 0);

        db.FinancialAccounts.Add(account);
        return account;
    }
}

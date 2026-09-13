using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Domain.Providers;

namespace Nexo.Application.Providers;

/// <summary>
/// What the "add account" screen is allowed to show. Capabilities come from the
/// catalogue, never from the client, so the app can never offer an automatic
/// connection that does not exist yet.
/// </summary>
public sealed record ProviderCapabilityDto(string Mode, string Label, bool IsAutomatic);

public sealed record ProviderDto(
    string Code,
    string Name,
    string ShortName,
    string Kind,
    string BrandColor,
    string? LogoKey,
    IReadOnlyList<ProviderCapabilityDto> Capabilities,
    string DefaultMode,
    bool SupportsStatementImport);

public interface IProviderCatalogService
{
    Task<IReadOnlyList<ProviderDto>> ListAsync(CancellationToken cancellationToken);
}

public sealed class ProviderCatalogService(INexoDbContext db) : IProviderCatalogService
{
    public async Task<IReadOnlyList<ProviderDto>> ListAsync(CancellationToken cancellationToken)
    {
        var providers = await db.Providers
            .AsNoTracking()
            // Registro rápido de efectivo: EFECTIVO queda fuera del catálogo de
            // "agregar cuenta" a propósito. No es una institución que se conecte, y
            // la cuenta de efectivo es una sola por persona -- la crea el propio
            // registro rápido la primera vez (CashAccountProvisioner). Si apareciera
            // aquí, cualquiera podría abrir un segundo "Efectivo" y partir su saldo
            // en dos sin que nada se lo advirtiera.
            .Where(p => p.IsActive && p.Code != ProviderCodes.Cash)
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return providers.Select(Map).ToList();
    }

    internal static ProviderDto Map(Provider provider) => new(
        provider.Code,
        provider.Name,
        provider.ShortName,
        provider.Kind.ToString(),
        provider.BrandColor,
        provider.LogoKey,
        provider.SupportedModes.Select(Describe).ToList(),
        provider.DefaultMode.ToString(),
        provider.Supports(ConnectionMode.ManualImport));

    private static ProviderCapabilityDto Describe(ConnectionMode mode) => mode switch
    {
        ConnectionMode.ManualImport => new ProviderCapabilityDto(mode.ToString(), "Importar movimientos", false),
        ConnectionMode.Email => new ProviderCapabilityDto(mode.ToString(), "Detección por correo", true),
        ConnectionMode.Api => new ProviderCapabilityDto(mode.ToString(), "Conexión automática", true),
        ConnectionMode.Webhook => new ProviderCapabilityDto(mode.ToString(), "Notificación en tiempo real", true),
        // Registro rápido de efectivo: escribir el movimiento a mano tampoco es
        // "automático" -- lo pone la persona, no una conexión.
        ConnectionMode.Manual => new ProviderCapabilityDto(mode.ToString(), "Registrar a mano", false),
        _ => new ProviderCapabilityDto(mode.ToString(), mode.ToString(), false),
    };
}

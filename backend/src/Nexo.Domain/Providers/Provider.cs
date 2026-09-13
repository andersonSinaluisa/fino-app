using Nexo.Domain.Common;

namespace Nexo.Domain.Providers;

public enum ProviderKind
{
    Bank = 0,
    Wallet = 1,
}

/// <summary>
/// How movements physically reach Nexo for a given provider.
/// Web-banking scraping is deliberately absent from this enum and is out of scope.
/// </summary>
public enum ConnectionMode
{
    ManualImport = 0,
    Email = 1,
    Api = 2,
    Webhook = 3,

    /// <summary>
    /// Registro rápido de efectivo: la persona escribe el movimiento a mano, uno
    /// a uno. Deliberadamente distinto de <see cref="ManualImport"/> (que sigue
    /// significando "sube un archivo del banco"): un proveedor con este modo no
    /// debe ofrecer nunca importar archivos ni conectar un correo.
    /// </summary>
    Manual = 4,
}

/// <summary>
/// Catalogue entry for a financial institution. Reference data owned by Nexo (not
/// by a user) and the single source of truth for what the UI may advertise: a
/// provider never claims an automatic connection that is not actually implemented.
/// </summary>
public sealed class Provider : Entity
{
    private Provider()
    {
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string ShortName { get; private set; } = null!;

    public ProviderKind Kind { get; private set; }

    public string CountryCode { get; private set; } = "EC";

    /// <summary>
    /// Comma-separated, persisted form of the supported modes. Kept as a scalar on
    /// purpose: the catalogue is tiny and a plain column keeps queries and
    /// migrations trivial.
    /// </summary>
    public string SupportedModesRaw { get; private set; } = nameof(ConnectionMode.ManualImport);

    /// <summary>Statement parser code used when <see cref="ConnectionMode.ManualImport"/> is available.</summary>
    public string? StatementParserCode { get; private set; }

    /// <summary>Brand colour used by the mobile client for the account avatar.</summary>
    public string BrandColor { get; private set; } = "#1D1D1B";

    public string? LogoKey { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int DisplayOrder { get; private set; }

    public IReadOnlyList<ConnectionMode> SupportedModes => ParseModes(SupportedModesRaw);

    public bool Supports(ConnectionMode mode) => SupportedModes.Contains(mode);

    public ConnectionMode DefaultMode
    {
        get
        {
            var modes = SupportedModes;
            return modes.Count > 0 ? modes[0] : ConnectionMode.ManualImport;
        }
    }

    /// <summary>True when at least one mode does not require the user to upload files.</summary>
    public bool HasAutomaticConnection =>
        SupportedModes.Any(m => m is ConnectionMode.Api or ConnectionMode.Webhook or ConnectionMode.Email);

    public static Provider Create(
        string code,
        string name,
        string shortName,
        ProviderKind kind,
        IEnumerable<ConnectionMode> supportedModes,
        DateTimeOffset now,
        string? statementParserCode = null,
        string brandColor = "#1D1D1B",
        string? logoKey = null,
        int displayOrder = 0)
    {
        var modes = supportedModes.Distinct().ToList();
        DomainException.Require(modes.Count > 0, "A provider must support at least one connection mode.");

        var provider = new Provider
        {
            Code = DomainException.RequireText(code, nameof(code), 40).ToUpperInvariant(),
            Name = DomainException.RequireText(name, nameof(name), 120),
            ShortName = DomainException.RequireText(shortName, nameof(shortName), 60),
            Kind = kind,
            SupportedModesRaw = FormatModes(modes),
            StatementParserCode = statementParserCode,
            BrandColor = brandColor,
            LogoKey = logoKey,
            DisplayOrder = displayOrder,
        };
        provider.Stamp(now);
        return provider;
    }

    public void UpdateCapabilities(IEnumerable<ConnectionMode> modes, DateTimeOffset now)
    {
        var list = modes.Distinct().ToList();
        DomainException.Require(list.Count > 0, "A provider must support at least one connection mode.");
        SupportedModesRaw = FormatModes(list);
        Stamp(now);
    }

    public static string FormatModes(IEnumerable<ConnectionMode> modes) =>
        string.Join(',', modes.Select(m => m.ToString()));

    public static IReadOnlyList<ConnectionMode> ParseModes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var result = new List<ConnectionMode>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<ConnectionMode>(part, ignoreCase: true, out var mode) && !result.Contains(mode))
            {
                result.Add(mode);
            }
        }

        return result;
    }
}

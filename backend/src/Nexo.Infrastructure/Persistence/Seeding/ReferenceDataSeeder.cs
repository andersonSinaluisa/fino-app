using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.EmailIngestion;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;

namespace Nexo.Infrastructure.Persistence.Seeding;

/// <summary>
/// Reference data that Nexo owns: the provider catalogue, the system categories,
/// the starting rule set and the trusted email senders. Idempotent — it runs on
/// every startup and only inserts what is missing.
/// </summary>
public sealed class ReferenceDataSeeder(NexoDbContext db, IClock clock, ILogger<ReferenceDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        await SeedProvidersAsync(now, cancellationToken);
        await SeedCategoriesAsync(now, cancellationToken);
        await SeedRulesAsync(now, cancellationToken);
        await SeedTrustedSendersAsync(now, cancellationToken);

        logger.LogInformation("Reference data is up to date.");
    }

    private async Task SeedProvidersAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Providers.Select(p => p.Code).ToListAsync(cancellationToken);

        // Only ManualImport is claimed today. Email and API modes are added here the
        // day their parsers and credentials actually exist, and not one day earlier.
        var catalogue = new[]
        {
            Provider.Create(ProviderCodes.Pichincha, "Banco Pichincha", "Pichincha", ProviderKind.Bank,
                [ConnectionMode.ManualImport], now, "PICHINCHA_V1", "#FFD100", "pichincha", 1),
            Provider.Create(ProviderCodes.Guayaquil, "Banco Guayaquil", "Guayaquil", ProviderKind.Bank,
                [ConnectionMode.ManualImport], now, "GUAYAQUIL_V1", "#E4007C", "guayaquil", 2),
            Provider.Create(ProviderCodes.Produbanco, "Produbanco", "Produbanco", ProviderKind.Bank,
                [ConnectionMode.ManualImport], now, "PRODUBANCO_V1", "#00953B", "produbanco", 3),
            Provider.Create(ProviderCodes.Pacifico, "Banco del Pacífico", "Pacífico", ProviderKind.Bank,
                [ConnectionMode.ManualImport], now, "PACIFICO_V1", "#0072CE", "pacifico", 4),
            Provider.Create(ProviderCodes.Deuna, "DEUNA", "DEUNA", ProviderKind.Wallet,
                [ConnectionMode.ManualImport], now, "GENERIC_V1", "#7B2FF7", "deuna", 5),
            Provider.Create(ProviderCodes.PayPhone, "PayPhone", "PayPhone", ProviderKind.Wallet,
                [ConnectionMode.ManualImport], now, "GENERIC_V1", "#00C2A8", "payphone", 6),
            Provider.Create(ProviderCodes.PeiGo, "PeiGo", "PeiGo", ProviderKind.Wallet,
                [ConnectionMode.ManualImport], now, "GENERIC_V1", "#FF5A36", "peigo", 7),
            // Registro rápido de efectivo: el dinero en el bolsillo entra al modelo
            // como un proveedor más para no necesitar ni una sola rama especial en
            // saldos, Home, Movimientos o Estadísticas. Su único modo es Manual, así
            // que jamás ofrece importar un archivo ni conectar un correo, y no tiene
            // parser de estados de cuenta (statementParserCode queda en null a
            // propósito: no existe un "estado de cuenta del efectivo").
            Provider.Create(ProviderCodes.Cash, "Efectivo", "Efectivo", ProviderKind.Wallet,
                [ConnectionMode.Manual], now, null, "#C7F36B", "efectivo", 0),
            Provider.Create(ProviderCodes.Other, "Otra institución", "Otra", ProviderKind.Bank,
                [ConnectionMode.ManualImport], now, "GENERIC_V1", "#74766F", null, 99),
        };

        var missing = catalogue.Where(p => !existing.Contains(p.Code)).ToArray();
        if (missing.Length > 0)
        {
            db.Providers.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedCategoriesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Categories
            .Where(c => c.UserId == null)
            .Select(c => c.Code)
            .ToListAsync(cancellationToken);

        var catalogue = new[]
        {
            Category.Builtin(CategoryCodes.Food, "Comida", "utensils", "#E4A853", now, false, 1),
            Category.Builtin(CategoryCodes.Groceries, "Supermercado", "shopping-cart", "#8DD9B6", now, false, 2),
            Category.Builtin(CategoryCodes.Transport, "Transporte", "car", "#7FB3E8", now, false, 3),
            Category.Builtin(CategoryCodes.Utilities, "Servicios", "zap", "#C7F36B", now, false, 4),
            Category.Builtin(CategoryCodes.Entertainment, "Entretenimiento", "film", "#D8A0E8", now, false, 5),
            Category.Builtin(CategoryCodes.Health, "Salud", "heart", "#D8665B", now, false, 6),
            Category.Builtin(CategoryCodes.Education, "Educación", "book", "#9AA8E8", now, false, 7),
            Category.Builtin(CategoryCodes.Shopping, "Compras", "shopping-bag", "#E8B4A0", now, false, 8),
            Category.Builtin(CategoryCodes.Subscriptions, "Suscripciones", "repeat", "#B6A0E8", now, false, 9),
            Category.Builtin(CategoryCodes.Transfers, "Transferencias", "arrow-left-right", "#ECE9E1", now, false, 10),
            Category.Builtin(CategoryCodes.Fees, "Comisiones e impuestos", "percent", "#A67C52", now, false, 11),
            Category.Builtin(CategoryCodes.Interest, "Intereses", "percent", "#C98A5B", now, false, 14),
            Category.Builtin(CategoryCodes.Income, "Ingresos", "trending-up", "#4E9F73", now, true, 12),
            Category.Builtin(CategoryCodes.Other, "Otros", "circle", "#74766F", now, false, 13),
        };

        var missing = catalogue.Where(c => !existing.Contains(c.Code)).ToArray();
        if (missing.Length > 0)
        {
            db.Categories.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedRulesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Was "skip entirely once any system rule exists", which meant a rule added
        // to the catalogue below (e.g. the 2026-09 batch for TRANSF/TRF, Fees, real
        // bank names) would never reach a database that had already been seeded
        // once -- exactly the gap that let Anderson's real statements keep missing
        // rules this file already declared. Keyed like SeedCategoriesAsync/
        // SeedProvidersAsync now: only what is actually missing gets inserted.
        var existingKeys = await db.CategorizationRules
            .Where(r => r.IsSystem)
            .Select(r => new { r.Pattern, r.MerchantPattern })
            .ToListAsync(cancellationToken);
        var existing = existingKeys
            .Select(r => (r.Pattern, MerchantPattern: r.MerchantPattern ?? string.Empty))
            .ToHashSet();

        var categories = await db.Categories
            .Where(c => c.UserId == null)
            .ToDictionaryAsync(c => c.Code, c => c.Id, cancellationToken);

        // Merchants an Ecuadorian user actually sees on a statement. Every entry is
        // a plain, editable rule — no model, nothing hidden.
        var seeds = new (string Pattern, string CategoryCode, TransactionDirection? Direction)[]
        {
            ("SUPERMAXI", CategoryCodes.Groceries, null),
            ("MEGAMAXI", CategoryCodes.Groceries, null),
            ("MI COMISARIATO", CategoryCodes.Groceries, null),
            ("TIA", CategoryCodes.Groceries, null),
            ("SANTA MARIA", CategoryCodes.Groceries, null),
            ("CORAL", CategoryCodes.Groceries, null),
            ("UBER", CategoryCodes.Transport, null),
            ("CABIFY", CategoryCodes.Transport, null),
            ("INDRIVE", CategoryCodes.Transport, null),
            ("PRIMAX", CategoryCodes.Transport, null),
            ("PETROECUADOR", CategoryCodes.Transport, null),
            ("MOBIL", CategoryCodes.Transport, null),
            ("NETFLIX", CategoryCodes.Subscriptions, null),
            ("SPOTIFY", CategoryCodes.Subscriptions, null),
            ("DISNEY", CategoryCodes.Subscriptions, null),
            ("YOUTUBE PREMIUM", CategoryCodes.Subscriptions, null),
            ("APPLE COM BILL", CategoryCodes.Subscriptions, null),
            ("GOOGLE", CategoryCodes.Subscriptions, null),
            ("AWS", CategoryCodes.Subscriptions, null),
            ("OPENAI", CategoryCodes.Subscriptions, null),
            ("CLARO", CategoryCodes.Utilities, null),
            ("MOVISTAR", CategoryCodes.Utilities, null),
            ("CNT", CategoryCodes.Utilities, null),
            ("ELECTRICA", CategoryCodes.Utilities, null),
            ("INTERAGUA", CategoryCodes.Utilities, null),
            ("EMPRESA ELECTRICA", CategoryCodes.Utilities, null),
            ("RAPPI", CategoryCodes.Food, null),
            ("PEDIDOSYA", CategoryCodes.Food, null),
            ("KFC", CategoryCodes.Food, null),
            ("MCDONALDS", CategoryCodes.Food, null),
            ("BURGER KING", CategoryCodes.Food, null),
            ("SWEET COFFEE", CategoryCodes.Food, null),
            ("JUAN VALDEZ", CategoryCodes.Food, null),
            ("FYBECA", CategoryCodes.Health, null),
            ("PHARMACYS", CategoryCodes.Health, null),
            ("SANA SANA", CategoryCodes.Health, null),
            ("CRUZ AZUL", CategoryCodes.Health, null),
            ("DE PRATI", CategoryCodes.Shopping, null),
            ("ETAFASHION", CategoryCodes.Shopping, null),
            ("MERCADO LIBRE", CategoryCodes.Shopping, null),
            ("AMAZON", CategoryCodes.Shopping, null),
            ("CINEMARK", CategoryCodes.Entertainment, null),
            ("SUPERCINES", CategoryCodes.Entertainment, null),
            // Real Guayaquil mall on Vía a Daule -- same rationale as DE PRATI/
            // ETAFASHION above (a public retail brand, not personal data). The
            // trailing "ING" is dropped because a real export truncated it to
            // "SHOPP"; the pattern still matches the untruncated spelling too.
            ("ISLA SHOPP", CategoryCodes.Shopping, null),
            // "TRANSFERENCIA" alone missed the abbreviated form Ecuadorian banks
            // actually print on most rows ("TRANSF. DIRECTA A ...", "TRF INTERBANCARIA
            // ..."): normalization strips the period, leaving "TRANSF"/"TRF", neither of
            // which contains the full 13-letter word. Both abbreviations are added
            // explicitly rather than relying on the longer pattern's substring, since a
            // description can just as easily read "TRANSF" only.
            ("TRANSFERENCIA", CategoryCodes.Transfers, null),
            ("TRANSF", CategoryCodes.Transfers, null),
            ("TRF", CategoryCodes.Transfers, null),
            // A real export's "Detalle" sometimes names the receiving/sending bank
            // itself instead of the word "Transferencia" -- e.g. a Guayaquil account
            // paying a person who banks at Pichincha shows "Banco Pichincha" as the
            // channel, with the actual counterparty only in "Beneficiario". None of
            // the patterns above catch that shape.
            ("BANCO PICHINCHA", CategoryCodes.Transfers, null),
            ("BANCO GUAYAQUIL", CategoryCodes.Transfers, null),
            ("BANCO PACIFICO", CategoryCodes.Transfers, null),
            ("PRODUBANCO", CategoryCodes.Transfers, null),
            ("BANCO BOLIVARIANO", CategoryCodes.Transfers, null),
            ("BANCO INTERNACIONAL", CategoryCodes.Transfers, null),
            // Pichincha's own mechanism for moving money to another bank's network
            // ("Cobro Interbancario ... para <destino>"). The amount on these rows
            // is a real transfer, not necessarily a fee -- when it IS the fee leg,
            // the line also says "Comisión" and the Fees rule below wins on
            // priority, so this is a safe default for the rest.
            ("COBRO INTERBANCARIO", CategoryCodes.Transfers, null),
            ("ROL DE PAGOS", CategoryCodes.Income, TransactionDirection.Income),
            ("SUELDO", CategoryCodes.Income, TransactionDirection.Income),
            ("NOMINA", CategoryCodes.Income, TransactionDirection.Income),
            ("DEPOSITO", CategoryCodes.Income, TransactionDirection.Income),
        };

        var rules = new List<CategorizationRule>();

        bool TryAdd(CategorizationRule rule)
        {
            var key = (rule.Pattern, MerchantPattern: rule.MerchantPattern ?? string.Empty);
            if (!existing.Add(key))
            {
                return false;
            }

            rules.Add(rule);
            return true;
        }

        foreach (var (pattern, categoryCode, direction) in seeds)
        {
            if (categories.TryGetValue(categoryCode, out var categoryId))
            {
                TryAdd(CategorizationRule.SystemRule(pattern, categoryId, now, RuleMatchKind.Contains, direction));
            }
        }

        // Bank fees and taxes ride along on the very same transfer/payment movements
        // ("COMISION TRANSFERENCIA INTERBANCARIA", "IVA COBRADO"), so a plain
        // "TRANSFERENCIA" rule would otherwise claim them first. Priority 900 (lower
        // runs first) makes these win that tie deterministically, without relying on
        // Specificity/MatchTextLength to sort it out.
        if (categories.TryGetValue(CategoryCodes.Fees, out var feesCategoryId))
        {
            foreach (var pattern in new[] { "COMISION", "IVA", "RETENCION" })
            {
                TryAdd(CategorizationRule.SystemRule(pattern, feesCategoryId, now, RuleMatchKind.Contains, priority: 900));
            }
        }

        // Entregable 14 ("Categorización v2"): a merchant-pattern rule so "UBER EATS"
        // wins over the plain "UBER" description rule above -- the spec's own
        // example ("UBER EATS -> Comida ; UBER -> Transporte"). A merchant match is
        // worth more Specificity than a bare description match, so this rule beats
        // "UBER" for any movement whose merchant guess contains "UBER EATS", while
        // a plain Uber ride (no "EATS" in the merchant) still falls to Transporte.
        if (categories.TryGetValue(CategoryCodes.Food, out var foodCategoryId))
        {
            TryAdd(CategorizationRule.SystemRule(
                pattern: string.Empty,
                categoryId: foodCategoryId,
                now: now,
                merchantPattern: "UBER EATS"));
        }

        if (rules.Count > 0)
        {
            db.CategorizationRules.AddRange(rules);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedTrustedSendersAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.TrustedSenders
            .Select(s => new { s.ProviderCode, s.Value })
            .ToListAsync(cancellationToken);

        // Domains only, and every one of them still requires the message to have
        // passed SPF/DKIM/DMARC upstream (see TrustedSender.Accepts).
        var catalogue = new[]
        {
            TrustedSender.Domain(ProviderCodes.Pichincha, "pichincha.com", now),
            TrustedSender.Domain(ProviderCodes.Guayaquil, "bancoguayaquil.com", now),
            TrustedSender.Domain(ProviderCodes.Produbanco, "produbanco.com", now),
            TrustedSender.Domain(ProviderCodes.Pacifico, "pacifico.fin.ec", now),
            TrustedSender.Domain(ProviderCodes.Deuna, "deuna.app", now),
            TrustedSender.Domain(ProviderCodes.PayPhone, "payphone.app", now),
            TrustedSender.Domain(ProviderCodes.PeiGo, "peigo.com.ec", now),
        };

        var missing = catalogue
            .Where(s => !existing.Any(e => e.ProviderCode == s.ProviderCode && e.Value == s.Value))
            .ToArray();

        if (missing.Length > 0)
        {
            db.TrustedSenders.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

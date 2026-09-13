using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.QuickEntry;

/// <summary>
/// §16-19: "FINO debe aprender de movimientos manuales anteriores."
///
/// La decisión de diseño está en el propio §16: <c>QuickEntrySuggestion</c> "no
/// necesita ser una entidad persistente separada si puede derivarse eficientemente
/// del historial". Aquí se deriva. No hay tabla, ni contador que mantener, ni un
/// segundo sitio donde un gasto pueda existir: si la persona borra el movimiento,
/// el frecuente se ajusta solo en la siguiente consulta, porque el frecuente ES el
/// historial, mirado de otra manera.
/// </summary>
public interface IQuickEntrySuggestionService
{
    /// <summary>
    /// Todo lo que el sheet necesita para abrirse, en una sola consulta (§40): la
    /// cuenta de efectivo, su saldo, y las sugerencias.
    /// </summary>
    Task<QuickEntryBootstrapDto> GetBootstrapAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class QuickEntrySuggestionService(INexoDbContext db, ICashAccountProvisioner cashAccounts)
    : IQuickEntrySuggestionService
{
    /// <summary>§17: "mostrar máximo 4 sugerencias."</summary>
    private const int MaxSuggestions = 4;

    /// <summary>
    /// §17 vs §19: por debajo de esto una etiqueta no es un hábito, es una
    /// casualidad. Dos veces basta para decir "otra vez lo mismo"; con una sola
    /// aparición estaríamos llamando "frecuente" a algo que pasó una vez.
    /// </summary>
    private const int MinOccurrencesForFrequent = 2;

    /// <summary>
    /// Cuántos movimientos manuales recientes se miran. Acotado a propósito: el
    /// hábito de hace ocho meses ya no es el hábito de hoy, y además mantiene la
    /// consulta barata sin importar cuántos años de historial haya.
    /// </summary>
    private const int HistoryWindow = 400;

    public async Task<QuickEntryBootstrapDto> GetBootstrapAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Solo lectura: abrir el sheet no debe crear una cuenta. Si todavía no hay
        // efectivo, se devuelve Guid.Empty y el cliente lo interpreta como "la
        // cuenta se creará al guardar el primer movimiento", que es exactamente lo
        // que hace CashAccountProvisioner.
        var cash = await cashAccounts.FindAsync(userId, cancellationToken);

        var history = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.Source == TransactionSource.Manual
                        && t.Status == TransactionStatus.Posted
                        // Una pata de transferencia no es un hábito de gasto. Sin
                        // esto, "Retiro en efectivo" -- el movimiento que Fino crea
                        // al conciliar un retiro -- acabaría ofreciéndose como
                        // frecuente, y tocarlo registraría un ingreso de la nada.
                        && !t.IsInternalTransfer)
            .OrderByDescending(t => t.TransactionDate)
            .Take(HistoryWindow)
            .Select(t => new HistoryRow(
                t.Description,
                t.NormalizedDescription,
                t.Amount,
                t.Direction,
                t.CategoryId,
                t.FinancialAccountId,
                t.TransactionDate))
            .ToListAsync(cancellationToken);

        var categories = await LoadCategoriesAsync(userId, history, cancellationToken);

        var groups = history
            // La etiqueta se agrupa por la descripción NORMALIZADA (la misma que ya
            // usan la deduplicación y las reglas), así que "Almuerzo", "almuerzo" y
            // "ALMUERZO " son un solo hábito y no tres. La dirección entra en la
            // clave porque "pago" como gasto y "pago" como ingreso no son lo mismo.
            .GroupBy(r => new { r.NormalizedDescription, r.Direction })
            .Where(g => !string.IsNullOrWhiteSpace(g.Key.NormalizedDescription))
            // Los textos por defecto ("Efectivo", "Ingreso en efectivo") son lo que
            // Fino pone cuando la persona NO escribió nada. Ofrecerlos como
            // "frecuente" sería ofrecer un botón que no significa nada.
            .Where(g => !QuickEntryDefaults.IsGenericLabel(g.Key.NormalizedDescription))
            .Select(g => Summarize(g.ToList(), categories))
            .ToList();

        var frequent = groups
            .Where(s => s.Frequency >= MinOccurrencesForFrequent)
            // Frecuencia primero, y a igual frecuencia lo más reciente: dos hábitos
            // de tres usos cada uno se ordenan por cuál sigue vivo.
            .OrderByDescending(s => s.Frequency)
            .ThenByDescending(s => s.LastUsedAt)
            .Take(MaxSuggestions)
            .ToList();

        // §19: los recientes son el plan B, así que no repiten lo que ya salió
        // arriba -- mostrar "Almuerzo" dos veces en el mismo sheet gastaría el
        // espacio de una sugerencia útil.
        var frequentKeys = frequent
            .Select(s => (s.Label, s.Direction))
            .ToHashSet();

        var recent = groups
            .Where(s => !frequentKeys.Contains((s.Label, s.Direction)))
            .OrderByDescending(s => s.LastUsedAt)
            .Take(MaxSuggestions)
            .ToList();

        return new QuickEntryBootstrapDto(
            cash?.Id ?? Guid.Empty,
            cash?.EstimatedBalance ?? 0m,
            cash?.LastVerifiedAt is not null,
            cash?.Currency ?? Currency.Usd,
            frequent,
            recent);
    }

    private async Task<Dictionary<Guid, Category>> LoadCategoriesAsync(
        Guid userId,
        IReadOnlyList<HistoryRow> history,
        CancellationToken cancellationToken)
    {
        var ids = history
            .Where(r => r.CategoryId is not null)
            .Select(r => r.CategoryId!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            // ADR-009: nunca `ids.Contains(c.Id)` sobre una columna Guid -- esa forma
            // no la puede traducir el proveedor de SQLite que usan los tests de
            // integración cuando se compone con el filtro global por usuario.
            .WhereIdIn(c => c.Id, ids)
            .ToListAsync(cancellationToken);

        return categories.ToDictionary(c => c.Id);
    }

    private static QuickEntrySuggestionDto Summarize(
        IReadOnlyList<HistoryRow> rows,
        IReadOnlyDictionary<Guid, Category> categories)
    {
        var newest = rows[0];

        // La etiqueta que se muestra es la del uso más reciente, con las mayúsculas
        // tal como la persona las escribió -- no la forma normalizada, que está
        // pensada para comparar, no para leer.
        var label = newest.Description.Trim();

        var amounts = rows.Select(r => r.Amount).OrderBy(a => a).ToArray();
        var typical = Median(amounts);

        // §17: usar el monto típico solo "si existe un typicalAmount confiable".
        // El criterio es cuánto se separan los montos de su mediana: un café de
        // 1.50 siempre es 1.50, pero "compras" puede ser 4 o 90, y proponer un
        // número ahí sería inventarlo. Se mide en proporción, no en dólares, para
        // que valga igual con montos de 1 que de 100.
        var reliable = IsAmountReliable(amounts, typical);

        // La categoría y la cuenta que se proponen son las del uso más reciente:
        // si la persona recategorizó su almuerzo la última vez, esa corrección es
        // justamente la que debe ganar.
        var categoryId = newest.CategoryId;
        var category = categoryId is { } id && categories.TryGetValue(id, out var found) ? found : null;

        return new QuickEntrySuggestionDto(
            label,
            newest.Direction.ToString(),
            category?.Id,
            category?.Name,
            category?.Icon,
            category?.Color,
            newest.FinancialAccountId,
            typical,
            reliable,
            rows.Count,
            newest.TransactionDate);
    }

    /// <summary>
    /// Mediana, no promedio (§16): tres almuerzos de 5 y una cena de 60 tienen
    /// mediana 5 y promedio 18,75. La mediana es "lo de siempre"; el promedio es
    /// un número que la persona no reconocería.
    /// </summary>
    private static decimal Median(decimal[] sorted)
    {
        if (sorted.Length == 0)
        {
            return 0m;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? MoneyMath.Round(sorted[middle])
            : MoneyMath.Round((sorted[middle - 1] + sorted[middle]) / 2m);
    }

    private static bool IsAmountReliable(decimal[] amounts, decimal typical)
    {
        if (amounts.Length == 0 || typical <= 0m)
        {
            return false;
        }

        if (amounts.Length == 1)
        {
            // Un solo uso: el monto es un dato real, no una estimación con dispersión
            // desconocida. Sirve como propuesta, y de todos modos esto solo se
            // alcanza en la lista de "recientes".
            return true;
        }

        // Desviación absoluta mediana relativa: qué tanto se aparta un uso típico de
        // la mediana. Se usa la mediana de las desviaciones (y no el máximo) para que
        // un único gasto raro no invalide un hábito por lo demás estable.
        var deviations = amounts
            .Select(a => Math.Abs(a - typical))
            .OrderBy(d => d)
            .ToArray();

        var medianDeviation = Median(deviations);
        return medianDeviation / typical <= 0.20m;
    }

    private sealed record HistoryRow(
        string Description,
        string NormalizedDescription,
        decimal Amount,
        TransactionDirection Direction,
        Guid? CategoryId,
        Guid FinancialAccountId,
        DateTimeOffset TransactionDate);
}

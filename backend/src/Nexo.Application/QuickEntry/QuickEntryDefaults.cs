using Nexo.Domain.Transactions;

namespace Nexo.Application.QuickEntry;

/// <summary>
/// Registro rápido de efectivo: los textos que Fino pone cuando la persona guarda
/// sin escribir nada (el camino "+ → 12.50 → Guardar", que es el normal).
///
/// Viven aquí, en un solo sitio, porque dos partes distintas los necesitan:
/// <c>QuickTransactionService</c> para escribirlos, y
/// <c>QuickEntrySuggestionService</c> para EXCLUIRLOS de los frecuentes -- un botón
/// "Efectivo" entre las sugerencias no diría nada, porque "Efectivo" es
/// precisamente lo que Fino escribe cuando no sabe qué fue. Si los textos se
/// duplicaran, cambiar uno haría aparecer basura en las sugerencias del otro.
/// </summary>
public static class QuickEntryDefaults
{
    public const string ExpenseDescription = "Efectivo";

    public const string IncomeDescription = "Ingreso en efectivo";

    public const string AdjustmentDescription = "Ajuste de efectivo";

    /// <summary>
    /// Las formas normalizadas de los textos anteriores, calculadas con el MISMO
    /// normalizador que usa la agrupación de sugerencias. Calculadas, no escritas a
    /// mano: <c>NormalizeForMatching</c> descarta tokens cortos y ruido, así que una
    /// constante escrita a ojo ("INGRESO EN EFECTIVO") podría no coincidir nunca con
    /// lo que el normalizador produce de verdad, y el filtro fallaría en silencio.
    /// </summary>
    public static readonly string[] NormalizedGenericLabels =
    [
        TextNormalizer.NormalizeForMatching(ExpenseDescription),
        TextNormalizer.NormalizeForMatching(IncomeDescription),
        TextNormalizer.NormalizeForMatching(AdjustmentDescription),
    ];

    public static bool IsGenericLabel(string? normalizedDescription) =>
        !string.IsNullOrWhiteSpace(normalizedDescription)
        && NormalizedGenericLabels.Contains(normalizedDescription, StringComparer.Ordinal);
}

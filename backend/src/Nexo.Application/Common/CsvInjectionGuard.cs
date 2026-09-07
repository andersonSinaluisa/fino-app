namespace Nexo.Application.Common;

/// <summary>
/// Every text value a statement parser reads (a description, a merchant, a
/// counterparty, a reference) came from someone else's file, not from Nexo's own
/// UI -- so it is untrusted input, in exactly the same sense a web form field
/// is. Excel, Google Sheets and LibreOffice all treat a cell beginning with
/// <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return as a
/// formula to evaluate, not as text to display. Nexo has no CSV/XLSX export of
/// transaction data today (Privacy's own export is JSON), so this is not fixing
/// an active exploit -- it is refusing to let one become possible the day such
/// an export ships, without anyone having to remember to escape it there.
/// </summary>
public static class CsvInjectionGuard
{
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// Prefixes a leading formula-trigger character with a single quote -- the
    /// same defusal every major spreadsheet respects as "treat this cell as
    /// text" -- so a value that reaches a future spreadsheet export renders as
    /// the literal text a bank statement actually put there, never as a formula.
    /// Values that do not start with a trigger character are returned unchanged.
    /// </summary>
    public static string Neutralize(string value) =>
        value.Length > 0 && FormulaTriggers.Contains(value[0])
            ? "'" + value
            : value;
}

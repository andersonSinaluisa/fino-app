namespace Nexo.Application.Imports.Parsing.Tabular;

/// <summary>A statement reduced to a rectangle of strings, whatever the file format was.</summary>
public sealed class TabularTable
{
    public TabularTable(IReadOnlyList<TabularRow> rows)
    {
        Rows = rows;
    }

    public IReadOnlyList<TabularRow> Rows { get; }

    public int RowCount => Rows.Count;
}

public sealed class TabularRow
{
    public TabularRow(int number, IReadOnlyList<string> cells)
    {
        Number = number;
        Cells = cells;
    }

    /// <summary>1-based line number inside the original file, used in error messages.</summary>
    public int Number { get; }

    public IReadOnlyList<string> Cells { get; }

    public string this[int index] => index >= 0 && index < Cells.Count ? Cells[index] : string.Empty;

    public bool IsEmpty => Cells.All(string.IsNullOrWhiteSpace);

    public string Join() => string.Join(" ", Cells.Where(c => !string.IsNullOrWhiteSpace(c)));
}

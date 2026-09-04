using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Nexo.Application.Imports.Parsing.Tabular;

/// <summary>
/// Minimal SpreadsheetML reader: opens the package, resolves the first worksheet
/// and returns its used range as text. A statement is a flat grid, so the ~150
/// lines here replace a heavyweight Excel dependency (ADR-002). Formulas are read
/// as their cached values, which is what statement exports always contain.
/// </summary>
public static class XlsxTableReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static bool LooksLikeXlsx(ReadOnlySpan<byte> header) =>
        header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B && (header[2] == 0x03 || header[2] == 0x05);

    public static TabularTable Read(Stream stream, int maxRows = 20000)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStrings = ReadSharedStrings(archive);
        var dateStyles = ReadDateStyleIndexes(archive);
        var sheetPath = ResolveFirstSheetPath(archive)
                        ?? throw new InvalidDataException("The workbook has no readable worksheet.");

        var entry = archive.GetEntry(sheetPath)
                    ?? throw new InvalidDataException($"Worksheet '{sheetPath}' is missing from the package.");

        using var sheetStream = entry.Open();
        var doc = XDocument.Load(sheetStream);

        var rows = new List<TabularRow>();
        foreach (var rowElement in doc.Descendants(Main + "row"))
        {
            if (rows.Count >= maxRows)
            {
                break;
            }

            var rowNumber = (int?)rowElement.Attribute("r") ?? rows.Count + 1;
            var cells = new List<string>();

            foreach (var cellElement in rowElement.Elements(Main + "c"))
            {
                var reference = (string?)cellElement.Attribute("r");
                var columnIndex = ColumnIndexFromReference(reference, cells.Count);

                while (cells.Count < columnIndex)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(ReadCellValue(cellElement, sharedStrings, dateStyles));
            }

            rows.Add(new TabularRow(rowNumber, cells.ToArray()));
        }

        return new TabularTable(rows);
    }

    private static string ReadCellValue(
        XElement cell,
        IReadOnlyList<string> sharedStrings,
        HashSet<int> dateStyles)
    {
        var type = (string?)cell.Attribute("t");

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value)).Trim();
        }

        var valueElement = cell.Element(Main + "v");
        if (valueElement is null)
        {
            return string.Empty;
        }

        var raw = valueElement.Value;

        if (type == "s")
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                   && index >= 0 && index < sharedStrings.Count
                ? sharedStrings[index]
                : string.Empty;
        }

        if (type is "str" or "e")
        {
            return raw.Trim();
        }

        if (type == "b")
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }

        var styleIndex = (int?)cell.Attribute("s") ?? -1;
        if (styleIndex >= 0
            && dateStyles.Contains(styleIndex)
            && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial)
            && ExcelSerialDate.TryConvert(serial, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return raw.Trim();
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        return doc.Root?
            .Elements(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)).Trim())
            .ToArray() ?? [];
    }

    /// <summary>
    /// Style indexes whose number format is a date/time one. Without this a date
    /// cell would come back as the raw serial number (e.g. 45536).
    /// </summary>
    private static HashSet<int> ReadDateStyleIndexes(ZipArchive archive)
    {
        var result = new HashSet<int>();
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry is null)
        {
            return result;
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        var customDateFormats = new HashSet<int>();
        foreach (var numFmt in doc.Descendants(Main + "numFmt"))
        {
            var id = (int?)numFmt.Attribute("numFmtId");
            var code = (string?)numFmt.Attribute("formatCode") ?? string.Empty;
            if (id is not null && LooksLikeDateFormat(code))
            {
                customDateFormats.Add(id.Value);
            }
        }

        var cellXfs = doc.Descendants(Main + "cellXfs").FirstOrDefault();
        if (cellXfs is null)
        {
            return result;
        }

        var index = 0;
        foreach (var xf in cellXfs.Elements(Main + "xf"))
        {
            var numFmtId = (int?)xf.Attribute("numFmtId") ?? 0;
            if (IsBuiltInDateFormat(numFmtId) || customDateFormats.Contains(numFmtId))
            {
                result.Add(index);
            }

            index++;
        }

        return result;
    }

    private static bool LooksLikeDateFormat(string code) =>
        code.Contains('y', StringComparison.OrdinalIgnoreCase)
        && (code.Contains('m', StringComparison.OrdinalIgnoreCase) || code.Contains('d', StringComparison.OrdinalIgnoreCase));

    private static bool IsBuiltInDateFormat(int numFmtId) =>
        numFmtId is (>= 14 and <= 22) or (>= 45 and <= 47);

    private static string? ResolveFirstSheetPath(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
        {
            return archive.Entries
                .FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)
                                     && e.FullName.EndsWith(".xml", StringComparison.Ordinal))?
                .FullName;
        }

        string? relationshipId;
        using (var stream = workbookEntry.Open())
        {
            var workbook = XDocument.Load(stream);
            var sheet = workbook.Descendants(Main + "sheet").FirstOrDefault();
            relationshipId = (string?)sheet?.Attribute(RelNs + "id");
        }

        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry is null || relationshipId is null)
        {
            return archive.Entries
                .FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal))?
                .FullName;
        }

        using var relsStream = relsEntry.Open();
        var rels = XDocument.Load(relsStream);
        var target = rels.Descendants(PkgRelNs + "Relationship")
            .FirstOrDefault(r => (string?)r.Attribute("Id") == relationshipId)?
            .Attribute("Target")?.Value;

        if (string.IsNullOrWhiteSpace(target))
        {
            return archive.Entries
                .FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal))?
                .FullName;
        }

        target = target.TrimStart('/');
        return target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target;
    }

    private static int ColumnIndexFromReference(string? reference, int fallback)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return fallback;
        }

        var index = 0;
        foreach (var ch in reference)
        {
            if (!char.IsAsciiLetter(ch))
            {
                break;
            }

            index = (index * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return index > 0 ? index - 1 : fallback;
    }
}

/// <summary>Excel stores dates as days since 1899-12-30, with a famous 1900 leap-year bug.</summary>
public static class ExcelSerialDate
{
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);

    public static bool TryConvert(double serial, out DateTime value)
    {
        value = default;
        if (serial <= 0 || serial > 2958465)
        {
            return false;
        }

        value = Epoch.AddDays(serial);
        return true;
    }
}

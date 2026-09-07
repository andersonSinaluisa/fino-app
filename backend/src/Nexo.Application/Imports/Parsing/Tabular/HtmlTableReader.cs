using System.Text;
using System.Text.RegularExpressions;

namespace Nexo.Application.Imports.Parsing.Tabular;

/// <summary>
/// Reads the single format Banco Guayaquil's personal-banking portal actually
/// exports: an HTML document -- a real, styled <c>&lt;table&gt;</c> with a
/// summary block above the movements -- saved with a ".xls" extension. Nothing
/// in the file name or declared content type can be trusted (see
/// <see cref="LooksLikeHtml"/>: detection reads the bytes, never the extension),
/// which is exactly the "detección segura por contenido" the requirement asks
/// for.
///
/// This is not a general HTML parser: it extracts every <c>&lt;tr&gt;</c> in the
/// document, in order, and every <c>&lt;td&gt;</c>/<c>&lt;th&gt;</c> inside each
/// one becomes exactly one cell -- entities decoded, inner tags stripped,
/// whitespace collapsed. <c>colspan</c>/<c>rowspan</c> are not expanded into
/// repeated cells: the summary rows above the movements use them, but those rows
/// are never a usable header (see <see cref="HeaderMappedStatementParser.TryFindHeader"/>)
/// and are skipped entirely; the movements table itself -- header and every data
/// row -- has no merged cells, so expansion buys nothing but complexity for a
/// statement file.
/// </summary>
public static class HtmlTableReader
{
    // A matchTimeout is a deliberate defensive backstop, not a workaround for a
    // known slow case: these patterns have no nested quantifiers to backtrack
    // catastrophically on, but a parser fed untrusted uploaded files should never
    // be able to hang a request no matter what arrives.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex RowPattern = new(
        "<tr\\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex CellPattern = new(
        "<t[hd]\\b[^>]*>(.*?)</t[hd]>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex WhitespacePattern = new("\\s+", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// True when the bytes look like an HTML document, whatever the file's
    /// extension or declared content type claim. Checks the first non-whitespace
    /// bytes for "&lt;html", "&lt;!doctype html" or a "&lt;table" tag, all
    /// case-insensitively and within the first few hundred bytes -- enough to
    /// clear a byte-order mark or a blank line without reading the whole file.
    /// </summary>
    public static bool LooksLikeHtml(ReadOnlySpan<byte> bytes)
    {
        var sample = bytes.Length > 1024 ? bytes[..1024] : bytes;

        // Decoding as UTF-8 is enough for a signature check even on a Latin-1
        // file: everything we look for below is plain ASCII, and a mis-decoded
        // accented byte later in the sample cannot turn a false match into a
        // true one or vice versa.
        var text = Encoding.UTF8.GetString(sample).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
               || text.Contains("<table", StringComparison.OrdinalIgnoreCase);
    }

    public static TabularTable Read(Stream stream, int maxRows = 20000, int maxColumns = CsvTableReader.DefaultMaxColumns)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        string html;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            html = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        else
        {
            try
            {
                html = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                html = Encoding.Latin1.GetString(bytes);
            }
        }

        var rows = new List<TabularRow>();
        var lineNumber = 1;

        foreach (Match rowMatch in RowPattern.Matches(html))
        {
            if (rows.Count >= maxRows)
            {
                break;
            }

            var cells = new List<string>();
            foreach (Match cellMatch in CellPattern.Matches(rowMatch.Groups[1].Value))
            {
                if (cells.Count >= maxColumns)
                {
                    throw new InvalidDataException(
                        $"El archivo tiene más de {maxColumns} columnas en una fila; no parece un estado de cuenta válido.");
                }

                cells.Add(CleanCell(cellMatch.Groups[1].Value));
            }

            if (cells.Count > 0)
            {
                rows.Add(new TabularRow(lineNumber, cells));
            }

            lineNumber++;
        }

        return new TabularTable(rows);
    }

    private static string CleanCell(string innerHtml)
    {
        // A "<br/>" inside one cell is a line break in the bank's own layout
        // (the instructions block), not a boundary between two values -- turn it
        // into a space rather than losing it, then strip every other tag.
        var withBreaksAsSpaces = innerHtml.Replace("<br", " <br", StringComparison.OrdinalIgnoreCase);
        var withoutTags = TagPattern.Replace(withBreaksAsSpaces, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespacePattern.Replace(decoded, " ").Trim();
    }
}

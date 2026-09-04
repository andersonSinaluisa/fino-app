using System.Text;

namespace Nexo.Application.Imports.Parsing.Tabular;

/// <summary>
/// RFC 4180 reader with delimiter sniffing. Written by hand rather than pulled from
/// a package because Ecuadorian bank exports mix comma and semicolon separators,
/// Latin-1 and UTF-8 encodings, and stray preamble lines — behaviour we need to
/// control precisely and test (see ADR-002).
/// </summary>
public static class CsvTableReader
{
    private static readonly char[] CandidateDelimiters = [';', ',', '\t', '|'];

    public static TabularTable Read(Stream stream, int maxRows = 20000)
    {
        var text = ReadAllText(stream);
        return ReadFromText(text, maxRows);
    }

    public static TabularTable ReadFromText(string text, int maxRows = 20000)
    {
        var delimiter = DetectDelimiter(text);
        var rows = new List<TabularRow>();
        var cells = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var lineNumber = 1;

        void CloseRow()
        {
            cells.Add(current.ToString().Trim());
            current.Clear();
            rows.Add(new TabularRow(lineNumber, cells.ToArray()));
            cells.Clear();
            lineNumber++;
        }

        for (var i = 0; i < text.Length && rows.Count < maxRows; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else if (ch == '\r')
            {
                // Swallow; the \n that follows closes the row.
            }
            else if (ch == '\n')
            {
                CloseRow();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0 || cells.Count > 0)
        {
            CloseRow();
        }

        return new TabularTable(rows);
    }

    /// <summary>
    /// Picks the delimiter that yields the most consistent column count across the
    /// first lines. More robust than counting occurrences, which quoted text skews.
    /// </summary>
    public static char DetectDelimiter(string text)
    {
        var sample = text.Split('\n').Take(25).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (sample.Length == 0)
        {
            return ',';
        }

        var best = ',';
        var bestScore = -1;

        foreach (var candidate in CandidateDelimiters)
        {
            var counts = sample.Select(line => CountOutsideQuotes(line, candidate)).ToArray();
            var max = counts.Max();
            if (max == 0)
            {
                continue;
            }

            var mode = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
            var score = (mode.Key * 10) + mode.Count();
            if (mode.Key > 0 && score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var inQuotes = false;
        var count = 0;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == delimiter && !inQuotes)
            {
                count++;
            }
        }

        return count;
    }

    private static string ReadAllText(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        // Strict UTF-8 first; fall back to Latin-1, which is what older exports use.
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}

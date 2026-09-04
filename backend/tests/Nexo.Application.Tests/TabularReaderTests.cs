using System.IO.Compression;
using System.Text;
using Nexo.Application.Imports.Parsing.Tabular;
using Xunit;

namespace Nexo.Application.Tests;

public class CsvTableReaderTests
{
    [Fact]
    public void Detects_a_semicolon_separated_file()
    {
        const string csv = "Fecha;Concepto;Debito;Credito\n04/03/2026;SUPERMAXI;48,20;\n";

        var table = CsvTableReader.ReadFromText(csv);

        Assert.Equal(2, table.RowCount);
        Assert.Equal("Concepto", table.Rows[0][1]);
        Assert.Equal("48,20", table.Rows[1][2]);
    }

    [Fact]
    public void Honours_quoted_fields_that_contain_the_delimiter()
    {
        const string csv = "Fecha,Concepto,Valor\n04/03/2026,\"SUPERMAXI, ALBORADA\",-48.20\n";

        var table = CsvTableReader.ReadFromText(csv);

        Assert.Equal("SUPERMAXI, ALBORADA", table.Rows[1][1]);
        Assert.Equal("-48.20", table.Rows[1][2]);
    }

    [Fact]
    public void Handles_escaped_quotes()
    {
        const string csv = "a,b\n\"say \"\"hi\"\"\",2\n";

        var table = CsvTableReader.ReadFromText(csv);

        Assert.Equal("say \"hi\"", table.Rows[1][0]);
    }

    [Fact]
    public void Reads_a_Latin1_encoded_stream_without_mangling_accents()
    {
        var bytes = Encoding.Latin1.GetBytes("Concepto\nTransferencia recibida de José\n");
        using var stream = new MemoryStream(bytes);

        var table = CsvTableReader.Read(stream);

        Assert.Equal("Transferencia recibida de José", table.Rows[1][0]);
    }

    [Fact]
    public void Skips_a_UTF8_byte_order_mark()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("Fecha,Valor\n04/03/2026,10\n"))
            .ToArray();

        using var stream = new MemoryStream(bytes);
        var table = CsvTableReader.Read(stream);

        Assert.Equal("Fecha", table.Rows[0][0]);
    }
}

public class XlsxTableReaderTests
{
    [Fact]
    public void Reads_shared_strings_inline_strings_and_numbers()
    {
        using var stream = new MemoryStream(XlsxBuilder.Build(
        [
            ["Fecha", "Concepto", "Valor"],
            ["2026-03-04", "SUPERMAXI ALBORADA", "-48.2"],
        ]));

        var table = XlsxTableReader.Read(stream);

        Assert.Equal(2, table.RowCount);
        Assert.Equal("Concepto", table.Rows[0][1]);
        Assert.Equal("SUPERMAXI ALBORADA", table.Rows[1][1]);
        Assert.Equal("-48.2", table.Rows[1][2]);
    }

    [Fact]
    public void Recognises_the_zip_signature()
    {
        var bytes = XlsxBuilder.Build([["a"]]);
        Assert.True(XlsxTableReader.LooksLikeXlsx(bytes.AsSpan(0, 4)));
        Assert.False(XlsxTableReader.LooksLikeXlsx("Fech"u8));
    }
}

/// <summary>
/// Builds a minimal but valid .xlsx package in memory, so the reader is exercised
/// against real SpreadsheetML rather than a mock.
/// </summary>
internal static class XlsxBuilder
{
    public static byte[] Build(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);

            Write(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);

            Write(archive, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Movimientos" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);

            Write(archive, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var sheet = new StringBuilder();
            sheet.Append("""<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

            for (var r = 0; r < rows.Count; r++)
            {
                sheet.Append($"<row r=\"{r + 1}\">");
                for (var c = 0; c < rows[r].Count; c++)
                {
                    var reference = $"{ColumnName(c)}{r + 1}";
                    var value = rows[r][c];

                    if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
                    {
                        sheet.Append($"<c r=\"{reference}\"><v>{value}</v></c>");
                    }
                    else
                    {
                        sheet.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c>");
                    }
                }

                sheet.Append("</row>");
            }

            sheet.Append("</sheetData></worksheet>");
            Write(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return buffer.ToArray();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        index++;
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            name = (char)('A' + remainder) + name;
            index = (index - 1) / 26;
        }

        return name;
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}

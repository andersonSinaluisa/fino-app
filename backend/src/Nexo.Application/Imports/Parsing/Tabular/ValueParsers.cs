using System.Globalization;

namespace Nexo.Application.Imports.Parsing.Tabular;

/// <summary>
/// Amounts in Ecuadorian statements appear as 1.234,56 and as 1,234.56, sometimes
/// with a currency symbol, sometimes parenthesised for negatives, sometimes with a
/// trailing minus. Guessing the decimal separator from the last punctuation mark
/// is the only reliable approach.
/// </summary>
public static class AmountParser
{
    public static bool TryParse(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        var negative = false;

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            negative = true;
            text = text[1..^1];
        }

        if (text.EndsWith('-'))
        {
            negative = true;
            text = text[..^1];
        }

        if (text.StartsWith('-'))
        {
            negative = true;
            text = text[1..];
        }

        if (text.StartsWith('+'))
        {
            text = text[1..];
        }

        var filtered = new string(text.Where(c => char.IsAsciiDigit(c) || c is '.' or ',').ToArray());
        if (filtered.Length == 0)
        {
            return false;
        }

        var lastDot = filtered.LastIndexOf('.');
        var lastComma = filtered.LastIndexOf(',');

        string normalized;
        if (lastDot < 0 && lastComma < 0)
        {
            normalized = filtered;
        }
        else if (lastComma > lastDot)
        {
            // Comma is the decimal separator: 1.234,56
            normalized = filtered.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else
        {
            // Dot is the decimal separator: 1,234.56
            normalized = filtered.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = negative ? -parsed : parsed;
        return true;
    }
}

public static class DateParser
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy",
        "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd/MM/yy", "d/M/yy",
        "MM/dd/yyyy", "dd/MMM/yyyy", "dd-MMM-yyyy", "dd MMM yyyy",
    ];

    private static readonly Dictionary<string, int> SpanishMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENE"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["ABR"] = 4, ["MAY"] = 5, ["JUN"] = 6,
        ["JUL"] = 7, ["AGO"] = 8, ["SEP"] = 9, ["OCT"] = 10, ["NOV"] = 11, ["DIC"] = 12,
    };

    /// <summary>Parses the calendar date only; the time-of-day is applied by the caller.</summary>
    public static bool TryParseDate(string? raw, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();

        if (DateTime.TryParseExact(
                text,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact))
        {
            value = exact.Date;
            return true;
        }

        if (TryParseSpanishLongDate(text, out var spanish))
        {
            value = spanish;
            return true;
        }

        // Excel leaves plain dates as serial numbers when the style is unreadable.
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial)
            && serial > 20000
            && ExcelSerialDate.TryConvert(serial, out var fromSerial))
        {
            value = fromSerial.Date;
            return true;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
        {
            value = loose.Date;
            return true;
        }

        return false;
    }

    private static bool TryParseSpanishLongDate(string text, out DateTime value)
    {
        value = default;
        var parts = text
            .Replace(" de ", " ", StringComparison.OrdinalIgnoreCase)
            .Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var day))
        {
            return false;
        }

        // Statements write the month as "ENE", "Ene.", "enero" or "marzo": the first
        // three letters are what identifies it in every one of those spellings.
        var monthToken = parts[1].TrimEnd('.');
        if (monthToken.Length > 3)
        {
            monthToken = monthToken[..3];
        }

        if (!SpanishMonths.TryGetValue(monthToken, out var month))
        {
            return false;
        }

        if (!int.TryParse(parts[2], out var year))
        {
            return false;
        }

        if (year < 100)
        {
            year += 2000;
        }

        if (day is < 1 or > 31 || year is < 1990 or > 2200)
        {
            return false;
        }

        try
        {
            value = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

/// <summary>
/// Statement dates are wall-clock dates in the account holder's timezone. Storing
/// them as UTC without that conversion is how movements silently jump a day, which
/// then breaks both monthly totals and deduplication.
/// </summary>
public sealed class StatementDateInterpreter(string timeZoneId)
{
    private readonly TimeZoneInfo _timeZone = Resolve(timeZoneId);

    public static StatementDateInterpreter Ecuador() => new("America/Guayaquil");

    public DateTimeOffset ToInstant(DateTime localDate, TimeSpan? timeOfDay = null)
    {
        var local = DateTime.SpecifyKind(localDate.Date.Add(timeOfDay ?? TimeSpan.FromHours(12)), DateTimeKind.Unspecified);
        var offset = _timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public DateTimeOffset StartOfDay(DateTime localDate) => ToInstant(localDate, TimeSpan.Zero);

    public DateTimeOffset ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _timeZone);

    public DateTime ToLocalDate(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _timeZone).Date;

    /// <summary>First instant of the local month containing <paramref name="instant"/>.</summary>
    public DateTimeOffset StartOfMonth(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, _timeZone);
        return ToInstant(new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero);
    }

    public DateTimeOffset StartOfPreviousMonth(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, _timeZone);
        var previous = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(-1);
        return ToInstant(previous, TimeSpan.Zero);
    }

    private static TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // Ecuador has no DST, so a fixed offset is a faithful fallback.
            return TimeZoneInfo.CreateCustomTimeZone("nexo-ec", TimeSpan.FromHours(-5), "Ecuador", "Ecuador");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("nexo-ec", TimeSpan.FromHours(-5), "Ecuador", "Ecuador");
        }
    }
}

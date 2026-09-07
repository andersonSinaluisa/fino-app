using Nexo.Application.Imports.Parsing.Tabular;
using Xunit;

namespace Nexo.Application.Tests;

public class AmountParserTests
{
    [Theory]
    [InlineData("48,20", 48.20)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("$ 1.234,56", 1234.56)]
    [InlineData("USD 350.00", 350.00)]
    [InlineData("(48.20)", -48.20)]
    [InlineData("48.20-", -48.20)]
    [InlineData("-1.500,00", -1500.00)]
    [InlineData("0,00", 0)]
    [InlineData("1200", 1200)]
    public void Parses_both_Ecuadorian_and_US_number_formats(string input, double expected)
    {
        Assert.True(AmountParser.TryParse(input, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData("-")]
    public void Rejects_values_that_are_not_amounts(string input) =>
        Assert.False(AmountParser.TryParse(input, out _));
}

public class DateParserTests
{
    [Theory]
    [InlineData("04/03/2026", 2026, 3, 4)]
    [InlineData("4/3/2026", 2026, 3, 4)]
    [InlineData("2026-03-04", 2026, 3, 4)]
    [InlineData("04-03-2026", 2026, 3, 4)]
    [InlineData("04/03/26", 2026, 3, 4)]
    [InlineData("4 de marzo 2026", 2026, 3, 4)]
    [InlineData("04 ENE 2026", 2026, 1, 4)]
    // Entregable 26 ("Parsers de correo"): the same single-digit-month/day, dash
    // separated shape DateTimeFormats' own comment attributes to Pichincha's real
    // web export ("2026-8-31, 12:51 PM"), here without the time-of-day part.
    [InlineData("2026-8-31", 2026, 8, 31)]
    public void Parses_the_date_formats_Ecuadorian_statements_use(string input, int year, int month, int day)
    {
        Assert.True(DateParser.TryParseDate(input, out var value));
        Assert.Equal(new DateTime(year, month, day), value);
    }

    [Fact]
    public void Parses_an_Excel_serial_number()
    {
        Assert.True(DateParser.TryParseDate("45717", out var value));
        Assert.Equal(2025, value.Year);
    }

    [Fact]
    public void Rejects_text_that_is_not_a_date() =>
        Assert.False(DateParser.TryParseDate("SALDO FINAL", out _));
}

public class StatementDateInterpreterTests
{
    private readonly StatementDateInterpreter _dates = StatementDateInterpreter.Ecuador();

    [Fact]
    public void A_statement_date_is_a_local_date_not_a_UTC_midnight()
    {
        // Critical case 6: a movement on 1 March in Guayaquil must not become
        // 28 February once stored, and must not fall into the previous month.
        var instant = _dates.StartOfDay(new DateTime(2026, 3, 1));

        Assert.Equal(new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero), instant);
        Assert.Equal(new DateTime(2026, 3, 1), _dates.ToLocalDate(instant));
    }

    [Fact]
    public void A_late_night_movement_stays_on_its_local_day()
    {
        var instant = _dates.ToInstant(new DateTime(2026, 3, 31), new TimeSpan(23, 30, 0));

        Assert.Equal(new DateTime(2026, 4, 1), instant.UtcDateTime.Date);
        Assert.Equal(new DateTime(2026, 3, 31), _dates.ToLocalDate(instant));
    }

    [Fact]
    public void Month_boundaries_are_computed_in_local_time()
    {
        var reference = new DateTimeOffset(2026, 3, 1, 2, 0, 0, TimeSpan.Zero); // 28 Feb 21:00 local

        Assert.Equal(new DateTimeOffset(2026, 2, 1, 5, 0, 0, TimeSpan.Zero), _dates.StartOfMonth(reference));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero), _dates.StartOfPreviousMonth(reference));
    }
}

using Nexo.Application.Deduplication;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// The rules that decide whether the user sees a movement once or twice.
/// This is the most consequential logic in the product, so it is tested directly,
/// without a database in the way.
/// </summary>
public class DeduplicationMatcherTests
{
    private static readonly Guid Account = Guid.Parse("0192f2c4-0000-7000-8000-000000000001");
    private static readonly Guid OtherAccount = Guid.Parse("0192f2c4-0000-7000-8000-0000000000ff");
    private static readonly DateTimeOffset Day = new(2026, 3, 4, 17, 0, 0, TimeSpan.Zero);

    private readonly DeduplicationMatcher _matcher = new(new DeduplicationOptions());

    private static IncomingMovement Incoming(
        decimal amount = 48.20m,
        string description = "SUPERMAXI ALBORADA",
        string? reference = null,
        DateTimeOffset? date = null,
        TransactionDirection direction = TransactionDirection.Expense,
        Guid? accountId = null)
    {
        var when = date ?? Day;
        var account = accountId ?? Account;

        return new IncomingMovement(
            account,
            "PICHINCHA",
            when,
            amount,
            direction,
            description,
            reference,
            TransactionFingerprint.Compute("PICHINCHA", account, reference, when, amount, direction, description));
    }

    private static DeduplicationMatcher.ExistingMovement Existing(
        decimal amount = 48.20m,
        string description = "SUPERMAXI ALBORADA",
        string? reference = null,
        DateTimeOffset? date = null,
        TransactionDirection direction = TransactionDirection.Expense,
        Guid? accountId = null,
        TransactionStatus status = TransactionStatus.Posted)
    {
        var when = date ?? Day;
        var account = accountId ?? Account;

        return new DeduplicationMatcher.ExistingMovement(
            Guid.CreateVersion7(),
            account,
            when,
            amount,
            direction,
            reference,
            TextNormalizer.NormalizeForMatching(description),
            TransactionFingerprint.Compute("PICHINCHA", account, reference, when, amount, direction, description),
            status);
    }

    [Fact]
    public void Critical_case_1_the_same_movement_twice_is_an_exact_match()
    {
        var existing = Existing();
        var result = _matcher.Match(Incoming(), [existing]);

        Assert.Equal(DuplicateMatchType.ExactMatch, result.MatchType);
        Assert.Equal(existing.Id, result.Match!.TransactionId);
        Assert.Equal("same_fingerprint", result.Match.Reason);
    }

    [Fact]
    public void Critical_case_2_email_then_statement_is_recognised_as_one_movement()
    {
        // The email said "Compra en SUPERMAXI ... tarjeta ****4821" at 18:12 with no
        // reference; the statement row says "SUPERMAXI ALBORADA GYE" the next morning.
        var fromEmail = Existing(
            48.20m,
            "Compra en SUPERMAXI ALBORADA con tu tarjeta terminada en 4821",
            date: new DateTimeOffset(2026, 3, 4, 23, 12, 0, TimeSpan.Zero));

        var fromStatement = Incoming(
            48.20m,
            "SUPERMAXI ALBORADA GYE",
            date: new DateTimeOffset(2026, 3, 5, 17, 0, 0, TimeSpan.Zero));

        var result = _matcher.Match(fromStatement, [fromEmail]);

        Assert.Equal(DuplicateMatchType.ProbableMatch, result.MatchType);
        Assert.Equal(fromEmail.Id, result.Match!.TransactionId);
    }

    [Fact]
    public void A_shared_bank_reference_matches_even_when_the_stored_fingerprint_is_from_an_older_recipe()
    {
        // Fingerprints carry a recipe version. After a recipe change the stored
        // value no longer matches a freshly computed one, and the bank reference is
        // what keeps the two records recognisable as the same movement.
        var existing = new DeduplicationMatcher.ExistingMovement(
            Guid.CreateVersion7(),
            Account,
            Day,
            48.20m,
            TransactionDirection.Expense,
            "TRX-99881",
            TextNormalizer.NormalizeForMatching("COMPRA TARJETA"),
            "v0-fingerprint-from-a-previous-recipe",
            TransactionStatus.Posted);

        var incoming = Incoming(48.25m, "SUPERMAXI ALBORADA", reference: "TRX-99881");

        var result = _matcher.Match(incoming, [existing]);

        Assert.Equal(DuplicateMatchType.ExactMatch, result.MatchType);
        Assert.Equal("same_bank_reference", result.Match!.Reason);
    }

    [Fact]
    public void The_same_reference_and_amount_match_on_the_fingerprint()
    {
        var existing = Existing(48.20m, "COMPRA TARJETA", reference: "TRX-99881");
        var incoming = Incoming(48.20m, "SUPERMAXI ALBORADA", reference: "TRX-99881");

        var result = _matcher.Match(incoming, [existing]);

        Assert.Equal(DuplicateMatchType.ExactMatch, result.MatchType);
        Assert.Equal("same_fingerprint", result.Match!.Reason);
    }

    [Fact]
    public void A_charge_that_shares_a_document_number_with_its_transfer_is_not_a_duplicate()
    {
        // Taken from a real Banco Pichincha statement: an interbank transfer, the
        // commission and the tax on that commission all carry one "Nro. Documento".
        // Treating the reference as an identity discarded two of the three.
        var transfer = Existing(116.00m, "TRANSFERENCIA INTERBANCARIA A PROVEEDOR", reference: "90000001");
        var commission = Incoming(0.36m, "COMISION TRANSFERENCIA INTERBANCARIA ENVIADA", reference: "90000001");
        var tax = Incoming(0.05m, "IVA COBRADO", reference: "90000001");

        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(commission, [transfer]).MatchType);
        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(tax, [transfer]).MatchType);
    }

    [Fact]
    public void Critical_case_5_an_expense_never_matches_an_income_of_the_same_amount()
    {
        var existing = Existing(direction: TransactionDirection.Income, description: "TRANSFERENCIA");
        var incoming = Incoming(direction: TransactionDirection.Expense, description: "TRANSFERENCIA");

        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(incoming, [existing]).MatchType);
    }

    [Fact]
    public void Critical_case_7_a_movement_without_a_reference_still_matches_on_its_content()
    {
        var existing = Existing(reference: null);
        var incoming = Incoming(reference: null);

        Assert.Equal(DuplicateMatchType.ExactMatch, _matcher.Match(incoming, [existing]).MatchType);
    }

    [Fact]
    public void Two_identical_purchases_in_different_accounts_are_two_movements()
    {
        var existing = Existing(accountId: OtherAccount);
        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(Incoming(), [existing]).MatchType);
    }

    [Fact]
    public void A_movement_outside_the_date_window_is_not_a_duplicate()
    {
        var existing = Existing(date: Day.AddDays(-10), description: "SUPERMAXI ALBORADA CENTRO");
        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(Incoming(), [existing]).MatchType);
    }

    [Fact]
    public void A_different_merchant_for_the_same_amount_and_day_is_not_a_duplicate()
    {
        var existing = Existing(48.20m, "PRIMAX VIA DAULE");
        var incoming = Incoming(48.20m, "SUPERMAXI ALBORADA");

        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(incoming, [existing]).MatchType);
    }

    [Fact]
    public void A_dismissed_movement_is_not_a_duplicate_candidate()
    {
        var existing = Existing(status: TransactionStatus.Ignored);
        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(Incoming(), [existing]).MatchType);
    }

    [Fact]
    public void An_amount_beyond_the_tolerance_is_a_different_movement()
    {
        var existing = Existing(48.20m);
        var incoming = Incoming(64.90m);

        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(incoming, [existing]).MatchType);
    }

    [Fact]
    public void Raising_the_threshold_makes_the_matcher_more_conservative()
    {
        var strict = new DeduplicationMatcher(new DeduplicationOptions { ProbableThreshold = 0.99d });

        var fromEmail = Existing(
            48.20m,
            "Compra en SUPERMAXI ALBORADA con tu tarjeta terminada en 4821",
            date: Day.AddHours(6));

        var result = strict.Match(Incoming(48.20m, "SUPERMAXI ALBORADA GYE", date: Day.AddDays(1)), [fromEmail]);

        Assert.Equal(DuplicateMatchType.NoMatch, result.MatchType);
    }

    [Fact]
    public void The_best_candidate_wins_when_several_are_close()
    {
        var far = Existing(48.20m, "SUPERMAXI CENTRO", date: Day.AddDays(3));
        var near = Existing(48.20m, "SUPERMAXI ALBORADA GYE", date: Day.AddDays(1));

        var result = _matcher.Match(Incoming(48.20m, "SUPERMAXI ALBORADA", date: Day), [far, near]);

        Assert.Equal(DuplicateMatchType.ProbableMatch, result.MatchType);
        Assert.Equal(near.Id, result.Match!.TransactionId);
    }

    [Fact]
    public void No_candidates_means_no_match() =>
        Assert.Equal(DuplicateMatchType.NoMatch, _matcher.Match(Incoming(), []).MatchType);

    [Fact]
    public void A_duplicate_near_the_UTC_day_boundary_is_still_caught_using_the_account_holders_calendar_day()
    {
        // Entregable 27: 10:00 local (Ecuador, UTC-5) on 1 March is 15:00 UTC the
        // same day -- no boundary crossing. 23:30 local on 4 March is 04:30 UTC on
        // 5 March -- it crosses midnight in UTC but not locally. Three local
        // calendar days apart (within the default DateWindowDays=3), but four UTC
        // calendar days apart. Comparing raw UTC dates would silently miss this as
        // NoMatch -- an undetected duplicate, worse than an unnecessary flag.
        var existing = Existing(48.20m, "SUPERMAXI ALBORADA", date: new DateTimeOffset(2026, 3, 1, 15, 0, 0, TimeSpan.Zero));
        var incoming = Incoming(48.20m, "SUPERMAXI ALBORADA", date: new DateTimeOffset(2026, 3, 5, 4, 30, 0, TimeSpan.Zero));

        var result = _matcher.Match(incoming, [existing]);

        Assert.Equal(DuplicateMatchType.ProbableMatch, result.MatchType);
        Assert.Equal(existing.Id, result.Match!.TransactionId);
    }
}

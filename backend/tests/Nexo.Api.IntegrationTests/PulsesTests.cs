using System.Net.Http.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// PULSO FASE 1: PulseEngine's seven detection rules, exercised through the real
/// API (POST /pulses/evaluate, GET /pulses) the same way InsightsTests exercises
/// InsightEngine -- real imported movements, a fixed clock, no mocked financial
/// data. FASE 3 additions exercise PulseNotificationDecisionService the same
/// way, through GET /api/v1/notifications -- push delivery itself (SentCount,
/// quiet hours) is NotificationsTests' job, since that is where SpyPushSender
/// already lives; here the only question is whether evaluating produced the
/// right in-app notification. FASE 4 additions at the end of this file cover
/// the two rules that shipped later (DailyClose, MonthEndProjection) and the
/// feedback endpoint. <see cref="NexoApiFactory.Now"/> is fixed at 10/03/2026,
/// noon Ecuador time -- DailyClose's "yesterday" is always 09/03/2026, and day
/// 10 is exactly MonthEndProjection's (and SpendingPace/SpendingImprovement's)
/// own "enough of the month has elapsed" floor.
/// </summary>
public class PulsesTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    [Fact]
    public async Task An_expense_far_above_the_users_typical_amount_produces_an_unusual_expense_pulse()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        // Fifteen ordinary expenses (median ~$20) establish the baseline PulseEngine
        // needs before it will call anything "unusual" -- confidence, not a guess.
        var historicalRows = string.Join(
            "\n",
            Enumerable.Range(1, 15).Select(i => $"{i:00}/01/2026,COMPRA DIARIA,TRX-H{i:000},20.00,"));

        var csv = $"""
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            {historicalRows}
            10/03/2026,TIENDA GRANDE,TRX-U001,85.00,
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        var unusual = pulses.EnumerateArray().FirstOrDefault(p => p.GetProperty("type").GetString() == "UnusualExpense");

        Assert.True(unusual.ValueKind != System.Text.Json.JsonValueKind.Undefined, "Expected an UnusualExpense pulse.");
        Assert.Equal(85.00m, unusual.GetProperty("value").GetDecimal());
        Assert.Equal(20.00m, unusual.GetProperty("comparisonValue").GetDecimal());
        Assert.Equal("Attention", unusual.GetProperty("severity").GetString());
        Assert.True(unusual.GetProperty("relevanceScore").GetDecimal() > 0m);
        // ¿Por qué veo esto? must be a real, filled-in explanation -- never blank.
        Assert.False(string.IsNullOrWhiteSpace(unusual.GetProperty("explanation").GetString()));
    }

    [Fact]
    public async Task Evaluating_twice_never_creates_a_second_pulse_for_the_same_transaction()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var historicalRows = string.Join(
            "\n",
            Enumerable.Range(1, 15).Select(i => $"{i:00}/01/2026,COMPRA DIARIA,TRX-D{i:000},20.00,"));

        var csv = $"""
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            {historicalRows}
            10/03/2026,TIENDA GRANDE,TRX-D900,90.00,
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");
        var afterFirst = (await user.GetJsonAsync("/api/v1/pulses")).GetArrayLength();

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");
        var afterSecond = (await user.GetJsonAsync("/api/v1/pulses")).GetArrayLength();

        Assert.Equal(afterFirst, afterSecond);
        Assert.True(afterFirst > 0);
    }

    [Fact]
    public async Task A_fresh_income_movement_with_no_history_produces_a_new_income_pulse()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            10/03/2026,ROL DE PAGOS,TRX-I001,,750.00
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        var income = pulses.EnumerateArray().FirstOrDefault(p => p.GetProperty("type").GetString() == "NewIncome");

        Assert.True(income.ValueKind != System.Text.Json.JsonValueKind.Undefined, "Expected a NewIncome pulse.");
        Assert.Equal(750.00m, income.GetProperty("value").GetDecimal());
        Assert.Equal("Positive", income.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task A_manual_account_that_has_never_synced_produces_an_account_outdated_pulse()
    {
        var user = await factory.RegisterUserAsync();
        // Mirrors InsightsTests' "never touched" account: created but no
        // statement ever imported into it, so LastSyncedAt stays null.
        var neverTouched = await user.CreateAccountAsync(openingBalance: null);

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        var outdated = pulses.EnumerateArray().FirstOrDefault(p => p.GetProperty("type").GetString() == "AccountOutdated");

        Assert.True(outdated.ValueKind != System.Text.Json.JsonValueKind.Undefined, "Expected an AccountOutdated pulse.");
        Assert.Equal(neverTouched.ToString(), outdated.GetProperty("referenceId").GetString());
        Assert.Equal("Neutral", outdated.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task A_single_pulse_can_be_fetched_by_id_for_the_detail_screen()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync(openingBalance: null);
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var list = await user.GetJsonAsync("/api/v1/pulses");
        var id = list.EnumerateArray().First().GetProperty("id").GetString();

        var single = await user.GetJsonAsync($"/api/v1/pulses/{id}");

        Assert.Equal(id, single.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(single.GetProperty("explanation").GetString()));
    }

    [Fact]
    public async Task Fetching_a_pulse_that_does_not_belong_to_the_user_returns_not_found()
    {
        var user = await factory.RegisterUserAsync();

        var response = await user.Client.GetAsync($"/api/v1/pulses/{Guid.CreateVersion7()}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Repeating_the_same_stale_account_within_the_cooldown_window_does_not_pulse_again()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync(openingBalance: null);

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");
        var afterFirst = (await user.GetJsonAsync("/api/v1/pulses"))
            .EnumerateArray().Count(p => p.GetProperty("type").GetString() == "AccountOutdated");

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");
        var afterSecond = (await user.GetJsonAsync("/api/v1/pulses"))
            .EnumerateArray().Count(p => p.GetProperty("type").GetString() == "AccountOutdated");

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, afterSecond);
    }

    [Fact]
    public async Task Pulses_are_listed_most_relevant_first()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();
        await user.CreateAccountAsync(openingBalance: null); // triggers a low-relevance AccountOutdated pulse

        var historicalRows = string.Join(
            "\n",
            Enumerable.Range(1, 15).Select(i => $"{i:00}/01/2026,COMPRA DIARIA,TRX-R{i:000},20.00,"));

        var csv = $"""
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            {historicalRows}
            10/03/2026,TIENDA GRANDE,TRX-R900,150.00,
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        var scores = pulses.EnumerateArray().Select(p => p.GetProperty("relevanceScore").GetDecimal()).ToList();

        Assert.True(scores.Count >= 2);
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    // -------------------------------------------------------------------
    // PULSO FASE 3: PulseNotificationDecisionService, exercised through
    // POST /pulses/evaluate + GET /api/v1/notifications.
    // -------------------------------------------------------------------

    [Fact]
    public async Task A_notify_worthy_pulse_produces_exactly_one_PulseReady_notification()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var historicalRows = string.Join(
            "\n",
            Enumerable.Range(1, 15).Select(i => $"{i:00}/01/2026,COMPRA DIARIA,TRX-N{i:000},20.00,"));

        var csv = $"""
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            {historicalRows}
            10/03/2026,TIENDA GRANDE,TRX-N900,150.00,
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var notifications = await user.GetJsonAsync("/api/v1/notifications");
        var pulseNotifications = notifications.EnumerateArray().Where(n => n.GetProperty("type").GetString() == "PulseReady").ToList();

        Assert.Single(pulseNotifications);
        Assert.Equal("Gasto fuera de lo común", pulseNotifications[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_neutral_severity_pulse_never_produces_a_notification()
    {
        var user = await factory.RegisterUserAsync();
        // Mirrors the AccountOutdated test above: the only pulse this produces
        // is Neutral severity, which PulseNotificationDecisionService's
        // severity gate excludes -- a standing condition is not news.
        await user.CreateAccountAsync(openingBalance: null);

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        Assert.True(pulses.GetArrayLength() > 0, "Expected the AccountOutdated pulse to still be persisted.");

        var notifications = await user.GetJsonAsync("/api/v1/notifications");
        Assert.Equal(0, notifications.GetArrayLength());
    }

    [Fact]
    public async Task Several_notify_worthy_pulses_from_one_pass_are_grouped_into_a_single_notification()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var historicalRows = string.Join(
            "\n",
            Enumerable.Range(1, 15).Select(i => $"{i:00}/01/2026,COMPRA DIARIA,TRX-G{i:000},20.00,"));

        // Two independent rules fire from this one import: DetectUnusualExpense
        // (the $150 expense, Attention) and DetectNewIncome (the income row is
        // this user's first-ever income signal, Positive). Grouping must turn
        // both into one push, never two.
        var csv = $"""
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            {historicalRows}
            10/03/2026,TIENDA GRANDE,TRX-G900,150.00,
            10/03/2026,ROL DE PAGOS,TRX-G901,,750.00
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        var nonNeutralCount = pulses.EnumerateArray().Count(p => p.GetProperty("severity").GetString() != "Neutral");
        Assert.Equal(2, nonNeutralCount);

        var notifications = await user.GetJsonAsync("/api/v1/notifications");
        var pulseNotifications = notifications.EnumerateArray().Where(n => n.GetProperty("type").GetString() == "PulseReady").ToList();

        Assert.Single(pulseNotifications);
        var grouped = pulseNotifications[0];
        Assert.Equal("2 novedades en tus finanzas", grouped.GetProperty("title").GetString());
        Assert.Contains("Gasto fuera de lo común", grouped.GetProperty("body").GetString());
        Assert.Contains("Ingreso nuevo", grouped.GetProperty("body").GetString());
    }

    // -------------------------------------------------------------------
    // PULSO FASE 4: DAILY_CLOSE.
    // -------------------------------------------------------------------

    [Fact]
    public async Task A_day_with_real_movements_produces_a_daily_close_pulse()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        // "Yesterday" relative to the fixed clock (10/03/2026) is 09/03/2026.
        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            09/03/2026,TIENDA X,TRX-DC001,45.00,
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        var close = pulses.EnumerateArray().FirstOrDefault(p => p.GetProperty("type").GetString() == "DailyClose");

        Assert.True(close.ValueKind != System.Text.Json.JsonValueKind.Undefined, "Expected a DailyClose pulse.");
        Assert.Equal(45.00m, close.GetProperty("value").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(close.GetProperty("explanation").GetString()));
    }

    [Fact]
    public async Task A_quiet_day_with_no_movements_produces_no_daily_close_pulse()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        // Real activity, but none of it on 09/03/2026 ("yesterday") -- a
        // quiet day must stay silent rather than summarize an empty one.
        var historicalRows = string.Join(
            "\n",
            Enumerable.Range(1, 15).Select(i => $"{i:00}/01/2026,COMPRA DIARIA,TRX-Q{i:000},20.00,"));

        var csv = $"""
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            {historicalRows}
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        Assert.DoesNotContain(pulses.EnumerateArray(), p => p.GetProperty("type").GetString() == "DailyClose");
    }

    [Fact]
    public async Task A_day_that_closed_with_more_income_than_expense_is_a_positive_daily_close()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            09/03/2026,ALMUERZO,TRX-DP001,10.00,
            09/03/2026,ROL DE PAGOS,TRX-DP002,,500.00
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        var close = pulses.EnumerateArray().First(p => p.GetProperty("type").GetString() == "DailyClose");

        Assert.Equal("Positive", close.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Evaluating_twice_never_creates_a_second_daily_close_for_the_same_day()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            09/03/2026,TIENDA X,TRX-DC900,45.00,
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");
        var afterFirst = (await user.GetJsonAsync("/api/v1/pulses"))
            .EnumerateArray().Count(p => p.GetProperty("type").GetString() == "DailyClose");

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");
        var afterSecond = (await user.GetJsonAsync("/api/v1/pulses"))
            .EnumerateArray().Count(p => p.GetProperty("type").GetString() == "DailyClose");

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, afterSecond);
    }

    // -------------------------------------------------------------------
    // PULSO FASE 4: MONTH_END_PROJECTION.
    // -------------------------------------------------------------------

    [Fact]
    public async Task A_months_spending_produces_a_projection_by_the_tenth_day()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        // $120 across the first 10 days of March -- a $12/day rate projected
        // over March's 31 days is $372.
        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            01/03/2026,COMPRA,TRX-M001,30.00,
            03/03/2026,COMPRA,TRX-M002,30.00,
            05/03/2026,COMPRA,TRX-M003,30.00,
            07/03/2026,COMPRA,TRX-M004,30.00,
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var pulses = await user.GetJsonAsync("/api/v1/pulses");
        var projection = pulses.EnumerateArray().FirstOrDefault(p => p.GetProperty("type").GetString() == "MonthEndProjection");

        Assert.True(projection.ValueKind != System.Text.Json.JsonValueKind.Undefined, "Expected a MonthEndProjection pulse.");
        Assert.Equal(372.00m, projection.GetProperty("value").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(projection.GetProperty("explanation").GetString()));
    }

    [Fact]
    public async Task Evaluating_twice_never_creates_a_second_month_end_projection()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            01/03/2026,COMPRA,TRX-P001,50.00,
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        await user.PostForJsonAsync("/api/v1/pulses/evaluate");
        var afterFirst = (await user.GetJsonAsync("/api/v1/pulses"))
            .EnumerateArray().Count(p => p.GetProperty("type").GetString() == "MonthEndProjection");

        // A second pass moments later, same clock: the cooldown ("at least a
        // day since the last one") blocks a re-fire even though nothing here
        // would have changed the estimate anyway.
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");
        var afterSecond = (await user.GetJsonAsync("/api/v1/pulses"))
            .EnumerateArray().Count(p => p.GetProperty("type").GetString() == "MonthEndProjection");

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, afterSecond);
    }

    // -------------------------------------------------------------------
    // PULSO FASE 4: feedback (👍/👎).
    // -------------------------------------------------------------------

    [Fact]
    public async Task Feedback_on_a_pulse_is_saved_and_can_be_changed()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync(openingBalance: null);
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var list = await user.GetJsonAsync("/api/v1/pulses");
        var id = list.EnumerateArray().First().GetProperty("id").GetString();

        var up = await user.Client.PostAsJsonAsync($"/api/v1/pulses/{id}/feedback", new { helpful = true });
        await up.EnsureOkAsync();
        var upBody = await up.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(upBody.GetProperty("feedbackHelpful").GetBoolean());

        // Changing your mind replaces the previous feedback rather than being rejected.
        var down = await user.Client.PostAsJsonAsync($"/api/v1/pulses/{id}/feedback", new { helpful = false });
        await down.EnsureOkAsync();
        var downBody = await down.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.False(downBody.GetProperty("feedbackHelpful").GetBoolean());

        var single = await user.GetJsonAsync($"/api/v1/pulses/{id}");
        Assert.False(single.GetProperty("feedbackHelpful").GetBoolean());
    }

    [Fact]
    public async Task A_pulse_with_no_feedback_yet_reports_it_as_null()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync(openingBalance: null);
        await user.PostForJsonAsync("/api/v1/pulses/evaluate");

        var list = await user.GetJsonAsync("/api/v1/pulses");
        var first = list.EnumerateArray().First();

        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("feedbackHelpful").ValueKind);
    }

    [Fact]
    public async Task Submitting_feedback_for_a_pulse_that_does_not_belong_to_the_user_returns_not_found()
    {
        var user = await factory.RegisterUserAsync();

        var response = await user.Client.PostAsJsonAsync($"/api/v1/pulses/{Guid.CreateVersion7()}/feedback", new { helpful = true });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}

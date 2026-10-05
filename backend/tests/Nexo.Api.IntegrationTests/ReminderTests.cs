using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Abstractions;
using Nexo.Application.Reminders;
using Nexo.Domain.Common;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Recordatorios end to end: real ledgers (cards, budgets, accounts, movements),
/// the real dispatcher, a spy push sender and a clock the test moves. Checks the
/// delivery policy: once per key, one action reminder per day, nothing "come
/// back" when the app was opened today, deep-link data in the history list.
/// NexoApiFactory.Now is Tuesday 10 mar 2026, 12:00 in Ecuador.
/// </summary>
public class ReminderTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class SpyPushSender : IPushSender
    {
        public List<PushMessage> Sent { get; } = [];

        public Task<PushSendResult> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
        {
            Sent.AddRange(messages);
            return Task.FromResult(new PushSendResult(messages.Count, []));
        }
    }

    private (WebApplicationFactory<Program> Host, FixedClock Clock, SpyPushSender Spy) Gated(DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        var spy = new SpyPushSender();
        var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(spy);
            }));
        return (host, clock, spy);
    }

    private static async Task<TestUser> RegisterAsync(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var email = $"reminders-{Guid.CreateVersion7():N}@nexo.test";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "NexoIntegration2026!",
            displayName = "Recordatorios",
            acceptedTerms = true,
            confirmedAdult = true,
        });
        await response.EnsureOkAsync();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            payload.GetProperty("accessToken").GetString());
        var user = new TestUser(payload.GetProperty("user").GetProperty("id").GetGuid(), email, payload.GetProperty("accessToken").GetString()!, client);

        await (await client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = $"ExponentPushToken[{Guid.CreateVersion7():N}]",
            platform = "Android",
        })).EnsureOkAsync();

        return user;
    }

    private static async Task<int> RunAsync(WebApplicationFactory<Program> host, Guid userId)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IReminderService>().RunAsync(userId, CancellationToken.None);
    }

    private static async Task SpendAsync(TestUser user, Guid accountId, decimal amount, string occurredAt)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new
        {
            amount,
            direction = "Expense",
            financialAccountId = accountId,
            leaveUncategorized = true,
            occurredAt = DateTimeOffset.Parse(occurredAt, System.Globalization.CultureInfo.InvariantCulture),
            description = "Compra",
        });
        await response.EnsureOkAsync();
    }

    private static async Task<JsonElement> NotificationsAsync(TestUser user) => await user.GetJsonAsync("/api/v1/notifications");

    [Fact]
    public async Task A_card_with_a_closed_statement_due_in_three_days_gets_one_payment_reminder_with_a_deep_link()
    {
        var (host, _, spy) = Gated(NexoApiFactory.Now);
        await using var _host = host;
        var user = await RegisterAsync(host);

        var created = await user.Client.PostAsJsonAsync("/api/v1/credit-cards", new
        {
            name = "Visa Pichincha",
            providerCode = "PICHINCHA",
            lastFour = "4582",
            creditLimit = 2000m,
            closingDay = 1,
            paymentDueDay = 13,
            autoReserve = true,
            network = "Visa",
        });
        await created.EnsureOkAsync();
        var cardId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("card").GetProperty("id").GetGuid();
        await SpendAsync(user, cardId, 247.03m, "2026-02-20T15:00:00Z");

        Assert.Equal(1, await RunAsync(host, user.Id));
        Assert.Equal(0, await RunAsync(host, user.Id));

        var message = Assert.Single(spy.Sent);
        Assert.Equal("Pago de tarjeta próximo", message.Title);
        Assert.Equal("Tu Visa Pichincha vence el 13 mar: $247.03.", message.Body);
        Assert.Equal(cardId.ToString(), message.Data!["cardId"]);

        var listed = (await NotificationsAsync(user))[0];
        Assert.Equal("CardPaymentDue", listed.GetProperty("type").GetString());
        Assert.Equal(cardId.ToString(), listed.GetProperty("data").GetProperty("cardId").GetString());
    }

    [Fact]
    public async Task One_action_reminder_per_day_and_come_back_reminders_wait_until_the_app_was_not_opened_that_day()
    {
        var (host, clock, spy) = Gated(NexoApiFactory.Now);
        await using var _host = host;
        var user = await RegisterAsync(host);
        var account = await user.CreateAccountAsync();

        var budget = await user.Client.PostAsJsonAsync("/api/v1/budgets", new { amount = 100m });
        await budget.EnsureOkAsync();
        var budgetId = (await budget.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("budget").GetProperty("id").GetGuid();
        await SpendAsync(user, account, 85m, "2026-03-09T15:00:00Z");

        // Day 1, 12:00: the budget crossed 80%.
        Assert.Equal(1, await RunAsync(host, user.Id));
        Assert.EndsWith(" va en 85% de su presupuesto.", spy.Sent[0].Body);
        Assert.Equal(budgetId.ToString(), spy.Sent[0].Data!["budgetId"]);
        Assert.Equal(0, await RunAsync(host, user.Id));

        // Night: nothing goes out.
        clock.UtcNow = new DateTimeOffset(2026, 3, 25, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, await RunAsync(host, user.Id));

        // 15 days later at 11:00 the account is stale and the app was not opened today.
        clock.UtcNow = new DateTimeOffset(2026, 3, 25, 16, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, await RunAsync(host, user.Id));
        Assert.Equal("Cuenta desactualizada", spy.Sent[^1].Title);
        Assert.Equal(account.ToString(), spy.Sent[^1].Data!["accountId"]);

        // Same day the budget goes over 100%: capped until tomorrow.
        await SpendAsync(user, account, 30m, "2026-03-24T15:00:00Z");
        Assert.Equal(0, await RunAsync(host, user.Id));

        clock.UtcNow = clock.UtcNow.AddDays(1);
        Assert.Equal(1, await RunAsync(host, user.Id));
        Assert.Equal("Presupuesto superado", spy.Sent[^1].Title);

        // The 80% warning never comes back after the 100% one.
        clock.UtcNow = clock.UtcNow.AddDays(1);
        Assert.Equal(0, await RunAsync(host, user.Id));
        Assert.Equal(3, spy.Sent.Count);
    }

    [Fact]
    public async Task A_stale_account_is_not_nagged_about_when_the_app_was_opened_today()
    {
        var (host, clock, spy) = Gated(NexoApiFactory.Now);
        await using var _host = host;
        var user = await RegisterAsync(host);
        await user.CreateAccountAsync();

        clock.UtcNow = NexoApiFactory.Now.AddDays(15);

        // Opening the app (device check-in) today.
        await (await user.Client.PostAsJsonAsync("/api/v1/notifications/devices", new
        {
            expoPushToken = "ExponentPushToken[opened-today]",
            platform = "Android",
        })).EnsureOkAsync();

        Assert.Equal(0, await RunAsync(host, user.Id));
        Assert.Empty(spy.Sent);
    }

    [Fact]
    public async Task The_weekly_summary_goes_out_on_sunday_evening_once_and_compares_with_the_previous_week()
    {
        // Sunday 15 mar 2026, 19:30 in Ecuador.
        var (host, _, spy) = Gated(new DateTimeOffset(2026, 3, 16, 0, 30, 0, TimeSpan.Zero));
        await using var _host = host;
        var user = await RegisterAsync(host);
        var account = await user.CreateAccountAsync();

        await SpendAsync(user, account, 60m, "2026-03-04T15:00:00Z");
        await SpendAsync(user, account, 40m, "2026-03-11T15:00:00Z");

        Assert.Equal(1, await RunAsync(host, user.Id));
        Assert.Equal(0, await RunAsync(host, user.Id));

        var message = Assert.Single(spy.Sent);
        Assert.Equal("Tu resumen semanal", message.Title);
        Assert.Equal("Esta semana gastaste $40.00, menos que la semana anterior.", message.Body);
        Assert.Equal("estadisticas", message.Data!["screen"]);
    }
}

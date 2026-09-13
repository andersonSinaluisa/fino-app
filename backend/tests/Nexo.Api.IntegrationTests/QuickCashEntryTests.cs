using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Registro rápido de efectivo, extremo a extremo por la API real.
///
/// La promesa del producto es "+ → monto → Guardar", así que la primera prueba es
/// literalmente esa: una petición con un solo campo tiene que producir un
/// movimiento válido. El resto cubre lo que hace honesta esa velocidad -- que
/// Deshacer devuelva el saldo donde estaba, que el doble toque no cobre dos veces,
/// y que el efectivo sea una cuenta de verdad y no un número aparte.
/// </summary>
public class QuickCashEntryTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Only_an_amount_is_enough_to_register_a_movement()
    {
        var user = await factory.RegisterUserAsync();

        var response = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new { amount = 12.50m });
        await response.EnsureOkAsync();

        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        // Los valores por defecto del §3, todos verificados de una vez: sin elegir
        // tipo es gasto, sin elegir cuenta es efectivo, sin escribir nada la
        // descripción es un texto neutro, y sin categoría el movimiento se guarda
        // igual en vez de bloquear.
        Assert.Equal(12.50m, created.GetProperty("amount").GetDecimal());
        Assert.Equal("Expense", created.GetProperty("direction").GetString());
        Assert.Equal(-12.50m, created.GetProperty("signedAmount").GetDecimal());
        Assert.Equal("Manual", created.GetProperty("source").GetString());
        Assert.Equal("Posted", created.GetProperty("status").GetString());
        Assert.Equal("Efectivo", created.GetProperty("accountAlias").GetString());
        Assert.Equal("EFECTIVO", created.GetProperty("providerCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task The_cash_account_is_created_once_and_reused()
    {
        var user = await factory.RegisterUserAsync();

        // Antes de registrar nada no debe existir ninguna cuenta: abrir Fino no
        // tiene por qué llenar Home de una cuenta "Efectivo · $0".
        var before = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Empty(before.EnumerateArray());

        var first = await CreateAsync(user, new { amount = 5m });
        var second = await CreateAsync(user, new { amount = 3m });

        Assert.Equal(
            first.GetProperty("financialAccountId").GetGuid(),
            second.GetProperty("financialAccountId").GetGuid());

        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Single(accounts.EnumerateArray());
        Assert.Equal("Cash", accounts[0].GetProperty("accountType").GetString());
    }

    [Fact]
    public async Task Cash_movements_move_the_cash_balance_like_any_other_account()
    {
        var user = await factory.RegisterUserAsync();

        // §23: "Efectivo: $50, gasto: $5, nuevo saldo: $45."
        await user.Client.PostAsJsonAsync("/api/v1/quick-entry/cash-balance", new { balance = 50m, mode = "Anchor" });
        await CreateAsync(user, new { amount = 5m });

        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Equal(45m, accounts[0].GetProperty("balance").GetDecimal());

        // Y un ingreso lo devuelve: el signo lo pone la dirección, nunca el monto.
        await CreateAsync(user, new { amount = 5m, direction = "Income" });

        accounts = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Equal(50m, accounts[0].GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Undo_removes_the_movement_and_restores_the_balance()
    {
        var user = await factory.RegisterUserAsync();
        await user.Client.PostAsJsonAsync("/api/v1/quick-entry/cash-balance", new { balance = 40m, mode = "Anchor" });

        var created = await CreateAsync(user, new { amount = 7.25m });
        var id = created.GetProperty("id").GetGuid();

        var afterCreate = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Equal(32.75m, afterCreate[0].GetProperty("balance").GetDecimal());

        var deleted = await user.Client.DeleteAsync($"/api/v1/quick-entry/transactions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Deshacer tiene que dejar las cosas EXACTAMENTE como estaban: ni el
        // movimiento ni su efecto en el saldo pueden sobrevivir.
        var afterUndo = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Equal(40m, afterUndo[0].GetProperty("balance").GetDecimal());

        var lookup = await user.Client.GetAsync($"/api/v1/transactions/{id}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task The_same_client_request_id_never_creates_two_movements()
    {
        var user = await factory.RegisterUserAsync();
        var clientRequestId = Guid.CreateVersion7();

        // §36: el doble toque en Guardar. Las dos peticiones son idénticas, como lo
        // serían de verdad, porque el id lo genera el cliente ANTES de mandar.
        var first = await CreateAsync(user, new { amount = 9m, clientRequestId });
        var second = await CreateAsync(user, new { amount = 9m, clientRequestId });

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());

        var list = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());

        // Y lo que importa de verdad: el saldo se movió una sola vez.
        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Equal(-9m, accounts[0].GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task A_negative_amount_is_rejected_instead_of_being_silently_flipped()
    {
        var user = await factory.RegisterUserAsync();

        var response = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new { amount = -5m });

        // Convertirlo a positivo en silencio produciría un gasto donde el cliente
        // quiso decir otra cosa. Mejor un 400 ruidoso que un saldo equivocado.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var zero = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new { amount = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    [Fact]
    public async Task An_existing_rule_categorises_a_handwritten_movement_too()
    {
        var user = await factory.RegisterUserAsync();

        // No se manda categoría: la decide el MISMO motor de reglas que categoriza
        // importaciones y correos. Esta es la prueba de que el registro rápido no
        // trae su propio diccionario de palabras clave.
        var created = await CreateAsync(user, new { amount = 6.80m, description = "UBER TRIP" });

        Assert.Equal("Transporte", created.GetProperty("categoryName").GetString());
        Assert.Equal("SystemRule", created.GetProperty("categorySource").GetString());
    }

    [Fact]
    public async Task An_unrecognised_description_is_saved_without_a_category_rather_than_forced_into_one()
    {
        var user = await factory.RegisterUserAsync();

        var created = await CreateAsync(user, new { amount = 4m, description = "Chochos de la esquina" });

        // §43: "no obligar a categorizar." Sin categoría honesta es mejor que
        // "Otros" puesto como si alguien lo hubiera decidido.
        Assert.Equal(JsonValueKind.Null, created.GetProperty("categoryId").ValueKind);
        Assert.Equal("Uncategorized", created.GetProperty("categorySource").GetString());
    }

    [Fact]
    public async Task A_bank_movement_cannot_be_edited_or_deleted_through_quick_entry()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            02/03/2026,SUPERMAXI ALBORADA,TRX-001,48.20,
            """;

        var items = await user.ConfirmAndListAsync(account, csv);
        var bankTransactionId = items[0].GetProperty("id").GetGuid();

        var deleted = await user.Client.DeleteAsync($"/api/v1/quick-entry/transactions/{bankTransactionId}");

        // Un movimiento que reportó el banco no es del usuario para borrarlo: se
        // ignora, que es otra operación y ya existe.
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);

        var edited = await user.Client.PutAsJsonAsync(
            $"/api/v1/quick-entry/transactions/{bankTransactionId}",
            new { amount = 1m });

        Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
    }

    [Fact]
    public async Task Correcting_the_cash_balance_records_a_visible_adjustment_instead_of_rewriting_history()
    {
        var user = await factory.RegisterUserAsync();

        await CreateAsync(user, new { amount = 8m, description = "Almuerzo" });

        // §25: Fino estima -8; la persona cuenta y tiene 35. La diferencia son 43.
        var response = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/cash-balance", new { balance = 35m });
        await response.EnsureOkAsync();

        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Equal(35m, accounts[0].GetProperty("balance").GetDecimal());

        var list = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());

        // El movimiento original sigue intacto -- "no modificar silenciosamente
        // movimientos históricos" -- y el ajuste existe como una fila propia y
        // legible, no como un cambio invisible del saldo.
        var descriptions = list.GetProperty("items")
            .EnumerateArray()
            .Select(t => t.GetProperty("description").GetString())
            .ToArray();

        Assert.Contains("Almuerzo", descriptions);
        Assert.Contains("Ajuste de efectivo", descriptions);
    }

    [Fact]
    public async Task Editing_a_handwritten_movement_recomputes_the_balance_from_the_stored_movements()
    {
        var user = await factory.RegisterUserAsync();
        var created = await CreateAsync(user, new { amount = 10m, description = "Bus" });
        var id = created.GetProperty("id").GetGuid();

        // Un gasto de 10 que pasa a ser un ingreso de 10 son 20 de diferencia: si el
        // saldo se "ajustara por la diferencia" a ojo quedaría en -20 o en 0.
        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/quick-entry/transactions/{id}",
            new { amount = 10m, direction = "Income", description = "Me pagaron" });

        await response.EnsureOkAsync();

        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Equal(10m, accounts[0].GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Repeated_movements_become_frequent_suggestions_with_a_typical_amount()
    {
        var user = await factory.RegisterUserAsync();

        await CreateAsync(user, new { amount = 5m, description = "Almuerzo" });
        await CreateAsync(user, new { amount = 5m, description = "almuerzo" });
        await CreateAsync(user, new { amount = 5.50m, description = "Almuerzo" });
        await CreateAsync(user, new { amount = 1.50m, description = "Café" });

        var bootstrap = await user.GetJsonAsync("/api/v1/quick-entry/bootstrap");
        var frequent = bootstrap.GetProperty("frequent").EnumerateArray().ToArray();

        // "Almuerzo", "almuerzo" y "Almuerzo" son UN hábito de tres usos, no tres de
        // uno: la agrupación es por descripción normalizada, la misma que usan la
        // deduplicación y las reglas.
        Assert.Single(frequent);
        Assert.Equal("Almuerzo", frequent[0].GetProperty("label").GetString());
        Assert.Equal(3, frequent[0].GetProperty("frequency").GetInt32());

        // Mediana de (5.00, 5.00, 5.50) = 5.00. El promedio sería 5.17, un número
        // que la persona no reconocería como "lo de siempre".
        Assert.Equal(5.00m, frequent[0].GetProperty("typicalAmount").GetDecimal());
        Assert.True(frequent[0].GetProperty("amountIsReliable").GetBoolean());

        // El café solo se usó una vez: es reciente, no frecuente.
        var recent = bootstrap.GetProperty("recent").EnumerateArray().ToArray();
        Assert.Contains(recent, r => r.GetProperty("label").GetString() == "Café");
        Assert.DoesNotContain(recent, r => r.GetProperty("label").GetString() == "Almuerzo");
    }

    [Fact]
    public async Task A_habit_with_wildly_varying_amounts_is_not_offered_with_a_typical_amount()
    {
        var user = await factory.RegisterUserAsync();

        await CreateAsync(user, new { amount = 4m, description = "Compras" });
        await CreateAsync(user, new { amount = 90m, description = "Compras" });
        await CreateAsync(user, new { amount = 12m, description = "Compras" });

        var bootstrap = await user.GetJsonAsync("/api/v1/quick-entry/bootstrap");
        var compras = bootstrap.GetProperty("frequent")[0];

        // §17: usar el monto típico "si existe un typicalAmount confiable". Aquí no
        // lo hay, así que el cliente debe abrir el sheet con la etiqueta puesta y el
        // monto en blanco en vez de guardar de un toque un número inventado.
        Assert.Equal("Compras", compras.GetProperty("label").GetString());
        Assert.False(compras.GetProperty("amountIsReliable").GetBoolean());
    }

    [Fact]
    public async Task Movements_saved_without_a_description_never_become_suggestions()
    {
        var user = await factory.RegisterUserAsync();

        await CreateAsync(user, new { amount = 3m });
        await CreateAsync(user, new { amount = 4m });
        await CreateAsync(user, new { amount = 5m });

        var bootstrap = await user.GetJsonAsync("/api/v1/quick-entry/bootstrap");

        // "Efectivo" es lo que Fino escribe cuando la persona NO escribió nada.
        // Ofrecerlo como frecuente sería ofrecer un botón que no significa nada.
        Assert.Empty(bootstrap.GetProperty("frequent").EnumerateArray());
        Assert.Empty(bootstrap.GetProperty("recent").EnumerateArray());
    }

    [Fact]
    public async Task One_persons_quick_entry_is_invisible_to_another()
    {
        var mine = await factory.RegisterUserAsync();
        var theirs = await factory.RegisterUserAsync();

        var created = await CreateAsync(mine, new { amount = 15m, description = "Almuerzo" });
        var id = created.GetProperty("id").GetGuid();

        var read = await theirs.Client.GetAsync($"/api/v1/transactions/{id}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var deleted = await theirs.Client.DeleteAsync($"/api/v1/quick-entry/transactions/{id}");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);

        var bootstrap = await theirs.GetJsonAsync("/api/v1/quick-entry/bootstrap");
        Assert.Empty(bootstrap.GetProperty("frequent").EnumerateArray());
        Assert.Empty(bootstrap.GetProperty("recent").EnumerateArray());
    }

    private static async Task<JsonElement> CreateAsync(TestUser user, object body)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", body);
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }
}

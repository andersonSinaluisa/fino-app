using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 3: closes the import state machine end to end, not just the happy
/// path. ImportFlowTests and DeduplicationFlowTests already prove
/// upload -> preview -> confirm -> transactions -> dashboard, and the Failed path
/// for an unreadable file; what neither one exercised is Cancelled, or the guards
/// that keep a finished import from being acted on again. Those gaps are exactly
/// where a "flujo funcional completo" would otherwise still need a manual DB fix
/// after a client bug — a double POST /confirm, a stale DELETE, a client racing
/// two tabs.
///
/// Import's real states (Nexo.Domain.Imports.Import) are Received, PreviewReady,
/// Completed, Cancelled and Failed. The plan's "Uploaded" and "Confirmed" are the
/// same states under the requirement's naming; there is no separately observable
/// "Parsed" state because parsing runs synchronously inside the upload request
/// (ADR-004: no queue, no worker in between) -- a client never sees an import sit
/// in a parsed-but-not-previewed state to begin with.
/// </summary>
public class ImportPipelineFlowTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Statement = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        01/03/2026,ACREDITACION ROL DE PAGOS,ROL-7001,,900.00
        02/03/2026,FARMACIA CRUZ AZUL,TRX-7001,15.50,
        """;

    [Fact]
    public async Task Fetching_an_import_by_id_returns_the_same_preview_the_upload_did()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var uploaded = await user.UploadStatementAsync(accountId, Statement);
        var importId = uploaded.GetProperty("importId").GetGuid();

        var fetched = await user.GetJsonAsync($"/api/v1/imports/{importId}");

        Assert.Equal("PreviewReady", fetched.GetProperty("status").GetString());
        Assert.Equal(
            uploaded.GetProperty("newRows").GetInt32(),
            fetched.GetProperty("newRows").GetInt32());
        Assert.Equal(2, fetched.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Cancelling_a_preview_discards_it_and_blocks_confirmation()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, Statement);
        var importId = preview.GetProperty("importId").GetGuid();

        var cancel = await user.Client.DeleteAsync($"/api/v1/imports/{importId}");
        await cancel.EnsureOkAsync();

        var afterCancel = await user.GetJsonAsync($"/api/v1/imports/{importId}");
        Assert.Equal("Cancelled", afterCancel.GetProperty("status").GetString());

        // A cancelled preview must not become confirmable behind the user's back --
        // this is the exact 409 ConfirmAsync raises for "already processed".
        var confirmAttempt = await user.Client.PostAsJsonAsync(
            $"/api/v1/imports/{importId}/confirm",
            new { excludedRowIds = Array.Empty<Guid>(), applyDeclaredClosingBalance = false });
        Assert.Equal(HttpStatusCode.Conflict, confirmAttempt.StatusCode);

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(0, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Confirming_an_already_completed_import_is_rejected_not_double_counted()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, Statement);
        var importId = preview.GetProperty("importId").GetGuid();
        await user.ConfirmImportAsync(importId);

        var secondConfirm = await user.Client.PostAsJsonAsync(
            $"/api/v1/imports/{importId}/confirm",
            new { excludedRowIds = Array.Empty<Guid>(), applyDeclaredClosingBalance = false });
        Assert.Equal(HttpStatusCode.Conflict, secondConfirm.StatusCode);

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(2, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_completed_import_cannot_be_cancelled_and_never_500s()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, Statement);
        var importId = preview.GetProperty("importId").GetGuid();
        await user.ConfirmImportAsync(importId);

        var cancelAttempt = await user.Client.DeleteAsync($"/api/v1/imports/{importId}");

        // Import.Cancel() raises a DomainException for this ("import_already_completed"),
        // which ProblemDetailsHandler maps to 422 -- never the bare 500 an unmapped
        // domain error would otherwise leak.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cancelAttempt.StatusCode);

        var stillThere = await user.GetJsonAsync($"/api/v1/imports/{importId}");
        Assert.Equal("Completed", stillThere.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_import_is_invisible_to_every_user_but_its_owner()
    {
        var owner = await factory.RegisterUserAsync();
        var accountId = await owner.CreateAccountAsync(openingBalance: null);
        var preview = await owner.UploadStatementAsync(accountId, Statement);
        var importId = preview.GetProperty("importId").GetGuid();

        var intruder = await factory.RegisterUserAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await intruder.Client.GetAsync($"/api/v1/imports/{importId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await intruder.Client.PostAsJsonAsync(
                $"/api/v1/imports/{importId}/confirm",
                new { excludedRowIds = Array.Empty<Guid>(), applyDeclaredClosingBalance = false })).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await intruder.Client.DeleteAsync($"/api/v1/imports/{importId}")).StatusCode);

        // And the owner's own preview survived every one of the intruder's attempts.
        var stillPreviewReady = await owner.GetJsonAsync($"/api/v1/imports/{importId}");
        Assert.Equal("PreviewReady", stillPreviewReady.GetProperty("status").GetString());
    }
}

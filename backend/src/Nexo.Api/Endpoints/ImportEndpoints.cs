using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.Imports;

namespace Nexo.Api.Endpoints;

public static class ImportEndpoints
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private static readonly string[] AllowedContentTypes =
    [
        "text/csv",
        "text/plain",
        "application/csv",
        "application/vnd.ms-excel",
        "application/octet-stream",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    ];

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/imports")
            .WithTags("Imports")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Uploads)
            .DisableAntiforgery();

        group.MapPost("", async (
            IFormFile file,
            Guid accountId,
            IImportService imports,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                throw ValidationException.For("file", "El archivo está vacío.");
            }

            if (file.Length > MaxUploadBytes)
            {
                throw new UnsupportedFileException("El archivo supera el límite de 10 MB.");
            }

            var contentType = file.ContentType ?? "application/octet-stream";
            if (!AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            {
                throw new UnsupportedFileException($"Tipo de archivo no soportado: {contentType}.");
            }

            await using var stream = file.OpenReadStream();

            var preview = await imports.UploadAsync(
                currentUser.RequireUserId(),
                accountId,
                Path.GetFileName(file.FileName),
                contentType,
                stream,
                cancellationToken);

            return Results.Ok(preview);
        })
        .WithSummary("Sube un estado de cuenta y devuelve el preview: nada se guarda como movimiento todavía.");

        // Entregable 10 (Generic CSV mapper): the same file, re-sent once the user
        // has told Nexo by hand which raw column is which -- shown only after a
        // POST above comes back Failed with unmappedColumns populated.
        group.MapPost("/manual", async (
            IFormFile file,
            Guid accountId,
            bool firstRowIsHeader,
            int dateColumn,
            int descriptionColumn,
            int? amountColumn,
            int? debitColumn,
            int? creditColumn,
            int? referenceColumn,
            bool saveMapping,
            IImportService imports,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                throw ValidationException.For("file", "El archivo está vacío.");
            }

            if (file.Length > MaxUploadBytes)
            {
                throw new UnsupportedFileException("El archivo supera el límite de 10 MB.");
            }

            var contentType = file.ContentType ?? "application/octet-stream";
            if (!AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            {
                throw new UnsupportedFileException($"Tipo de archivo no soportado: {contentType}.");
            }

            await using var stream = file.OpenReadStream();

            var mapping = new ManualColumnMapping(
                firstRowIsHeader, dateColumn, descriptionColumn, amountColumn, debitColumn, creditColumn, referenceColumn);

            var preview = await imports.UploadManualAsync(
                currentUser.RequireUserId(),
                accountId,
                Path.GetFileName(file.FileName),
                contentType,
                stream,
                mapping,
                saveMapping,
                cancellationToken);

            return Results.Ok(preview);
        })
        .WithSummary("Reenvía el archivo con columnas elegidas a mano cuando ningún parser lo reconoció.");

        group.MapGet("", async (
            int? page,
            int? pageSize,
            IImportService imports,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await imports.ListAsync(
                currentUser.RequireUserId(),
                new PageRequest { Page = page ?? 1, PageSize = pageSize ?? 30 },
                cancellationToken)))
            .WithSummary("Historial de importaciones del usuario, más recientes primero.");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IImportService imports,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await imports.GetPreviewAsync(currentUser.RequireUserId(), id, cancellationToken)));

        group.MapPost("/{id:guid}/confirm", async (
            Guid id,
            ConfirmImportRequest request,
            IImportService imports,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await imports.ConfirmAsync(currentUser.RequireUserId(), id, request, cancellationToken)))
            .WithSummary("Confirma el preview y escribe los movimientos.");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IImportService imports,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            await imports.CancelAsync(currentUser.RequireUserId(), id, cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}

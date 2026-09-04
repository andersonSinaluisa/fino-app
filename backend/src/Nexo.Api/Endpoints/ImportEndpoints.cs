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

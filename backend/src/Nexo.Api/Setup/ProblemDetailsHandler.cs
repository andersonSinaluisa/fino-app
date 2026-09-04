using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common;
using Nexo.Domain.Common;

namespace Nexo.Api.Setup;

/// <summary>
/// Turns exceptions into RFC 9457 problem details. Anything unexpected becomes a
/// generic 500 with a correlation id: the message may contain a description, an
/// amount or a file name, and none of that belongs in a client response.
/// </summary>
public sealed class ProblemDetailsHandler(
    IProblemDetailsService problemDetails,
    ILogger<ProblemDetailsHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.TraceIdentifier;

        var (status, title, code, detail) = exception switch
        {
            AppException app => (app.StatusCode, TitleFor(app.StatusCode), app.Code, app.Message),
            DomainException domain => (422, "No se pudo completar la operación", domain.Code, domain.Message),
            BadHttpRequestException => (400, "Solicitud inválida", "bad_request", "El cuerpo de la solicitud no es válido."),
            OperationCanceledException => (499, "Solicitud cancelada", "cancelled", "La solicitud fue cancelada."),
            _ => (500, "Error inesperado", "internal_error",
                "Ocurrió un error inesperado. Si vuelve a pasar, comparte este código con soporte."),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);
        }
        else
        {
            logger.LogInformation(
                "Request failed with {StatusCode} ({Code}). CorrelationId={CorrelationId}",
                status,
                code,
                correlationId);
        }

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://nexo.app/errors/{code}",
        };

        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = correlationId;

        if (exception is ValidationException validation && validation.Errors.Count > 0)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static string TitleFor(int status) => status switch
    {
        400 => "Solicitud inválida",
        401 => "No autenticado",
        403 => "Sin permisos",
        404 => "No encontrado",
        409 => "Conflicto",
        415 => "Archivo no soportado",
        _ => "Error",
    };
}

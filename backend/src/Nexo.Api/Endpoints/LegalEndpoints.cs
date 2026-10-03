using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.RegularExpressions;
using Nexo.Api.Setup;
using Nexo.Application.Abstractions;
using Nexo.Application.Legal;

namespace Nexo.Api.Endpoints;

public static class LegalEndpoints
{
    // Escapa < > & " ' pero deja tildes y ñ legibles en el HTML (UTF-8).
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/legal").WithTags("Legal");

        // Públicos: la app los muestra antes de que exista una cuenta.
        group.MapGet("/{kind}", (string kind, ILegalService legal) =>
                ParseKind(kind) is { } parsed ? Results.Ok(legal.GetDocument(parsed)) : Results.NotFound())
            .AllowAnonymous()
            .WithSummary("Términos ('terminos') o Política de privacidad ('privacidad') vigentes, en markdown.");

        group.MapGet("/status", async (ILegalService legal, ICurrentUser currentUser, CancellationToken cancellationToken) =>
                Results.Ok(await legal.GetStatusAsync(currentUser.RequireUserId(), cancellationToken)))
            .RequireAuthorization()
            .WithSummary("Versiones vigentes, cuáles aceptó la persona y su decisión sobre datos de uso.");

        group.MapPost("/accept", async (
                AcceptLegalRequest request,
                ILegalService legal,
                ICurrentUser currentUser,
                HttpContext http,
                CancellationToken cancellationToken) =>
                Results.Ok(await legal.AcceptAsync(currentUser.RequireUserId(), request, http.ToRequestContext(), cancellationToken)))
            .RequireAuthorization()
            .WithSummary("Acepta las versiones vigentes (400 si la app mostró una versión vieja).");

        group.MapPut("/analytics", async (
                AnalyticsConsentRequest request,
                ILegalService legal,
                ICurrentUser currentUser,
                HttpContext http,
                CancellationToken cancellationToken) =>
                Results.Ok(await legal.SetAnalyticsConsentAsync(currentUser.RequireUserId(), request.Granted, http.ToRequestContext(), cancellationToken)))
            .RequireAuthorization()
            .WithSummary("Da o retira el consentimiento para datos de uso anónimos.");

        // Página pública para App Store / Google Play: https://<api>/legal/privacidad
        app.MapGet("/legal/{kind}", (string kind, ILegalService legal) =>
                ParseKind(kind) is { } parsed
                    ? Results.Content(RenderHtml(legal.GetDocument(parsed)), "text/html; charset=utf-8")
                    : Results.NotFound())
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    private static LegalDocumentKind? ParseKind(string kind) => kind.ToLowerInvariant() switch
    {
        "terminos" or "terms" => LegalDocumentKind.Terms,
        "privacidad" or "privacy" => LegalDocumentKind.Privacy,
        _ => null,
    };

    /// <summary>
    /// Markdown mínimo (#, ##, -, **, párrafos) a HTML. Todo el texto se escapa
    /// antes de aplicar el formato, así que nada del contenido se interpreta como HTML.
    /// </summary>
    internal static string RenderHtml(LegalDocumentDto document)
    {
        var body = new StringBuilder();
        var inList = false;

        void CloseList()
        {
            if (inList)
            {
                body.Append("</ul>\n");
                inList = false;
            }
        }

        foreach (var raw in document.Markdown.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                CloseList();
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseList();
                body.Append("<h2>").Append(Inline(line[3..])).Append("</h2>\n");
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                CloseList();
                body.Append("<h1>").Append(Inline(line[2..])).Append("</h1>\n");
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (!inList)
                {
                    body.Append("<ul>\n");
                    inList = true;
                }

                body.Append("<li>").Append(Inline(line[2..])).Append("</li>\n");
            }
            else
            {
                CloseList();
                body.Append("<p>").Append(Inline(line)).Append("</p>\n");
            }
        }

        CloseList();

        return $$"""
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{Html.Encode(document.Title)}} · Fino</title>
<style>
  :root { color-scheme: light dark; --bg:#F5F3ED; --fg:#191A18; --muted:#74766F; }
  @media (prefers-color-scheme: dark) { :root { --bg:#121211; --fg:#ECE9E1; --muted:#A3A59D; } }
  body { margin:0; background:var(--bg); color:var(--fg); font:16px/1.6 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
  main { max-width:720px; margin:0 auto; padding:32px 20px 64px; }
  h1 { font-size:28px; line-height:1.2; margin:0 0 4px; }
  h2 { font-size:19px; margin:32px 0 8px; }
  p, li { color:var(--fg); }
  ul { padding-left:20px; }
  main > p:nth-of-type(1) { color:var(--muted); margin-top:0; }
</style>
</head>
<body><main>
{{body}}</main></body>
</html>
""";
    }

    private static string Inline(string text) =>
        Regex.Replace(Html.Encode(text), @"\*\*(.+?)\*\*", "<strong>$1</strong>");
}

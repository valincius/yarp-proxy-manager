using System.Text;
using ProxyManager.Application.Settings;

namespace ProxyManager.Api.Middleware;

/// <summary>
/// Serves the configured 404 page on the proxy port when no route matches.
/// Modes: Default (built-in page), Empty, or Custom (uploaded HTML template with
/// <c>{{host}}</c>, <c>{{path}}</c>, <c>{{method}}</c> and <c>{{now}}</c> placeholders).
/// Registered before routing so it observes the final response status.
/// </summary>
public sealed class NotFoundPageMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.StatusCode != StatusCodes.Status404NotFound || context.Response.HasStarted)
        {
            return;
        }

        var settings = context.RequestServices.GetRequiredService<SettingsService>();
        var config = await settings.GetNotFoundSettingsAsync(context.RequestAborted);

        var body = config.Mode switch
        {
            NotFoundModes.Empty => string.Empty,
            NotFoundModes.Custom => RenderTemplate(config.Template, context),
            _ => RenderDefault(context),
        };

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    private static string RenderTemplate(string template, HttpContext context) =>
        template
            .Replace("{{host}}", context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)
            .Replace("{{path}}", context.Request.Path.Value ?? "/", StringComparison.OrdinalIgnoreCase)
            .Replace("{{method}}", context.Request.Method, StringComparison.OrdinalIgnoreCase)
            .Replace("{{now}}", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss 'UTC'zzz"), StringComparison.OrdinalIgnoreCase);

    private static string RenderDefault(HttpContext context)
    {
        var host = context.Request.Host.Value;
        var path = context.Request.Path.Value ?? "/";
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("<title>404 — Not Found</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:system-ui,-apple-system,sans-serif;background:#f1f5f9;color:#0f172a;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}");
        html.AppendLine(".card{background:#fff;border:1px solid #e2e8f0;border-radius:12px;padding:48px;max-width:480px;text-align:center;box-shadow:0 1px 3px rgb(0 0 0 / .1)}");
        html.AppendLine("h1{font-size:56px;margin:0;color:#2563eb;font-weight:700}.code{font-size:14px;color:#64748b;margin-top:12px}.msg{color:#475569;margin-top:8px}");
        html.AppendLine("</style></head><body><div class=\"card\">");
        html.AppendLine("<h1>404</h1>");
        html.AppendLine("<div class=\"msg\">The page you requested could not be found.</div>");
        html.AppendLine($"<div class=\"code\">{System.Net.WebUtility.HtmlEncode(host)}{System.Net.WebUtility.HtmlEncode(path)}</div>");
        html.AppendLine("</div></body></html>");
        return html.ToString();
    }
}

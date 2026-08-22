using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProxyManager.Application.Settings;

namespace ProxyManager.Proxy;

/// <summary>
/// Captures per-hostname request statistics on the proxy port (status, duration, bytes,
/// client IP, optional request/response bodies) and feeds <see cref="TrafficMonitor"/>.
/// Registered first in the proxy branch so every proxy-port request is counted, including
/// redirects, access-list 403s, 404s and exceptions. The admin port is not counted.
/// </summary>
public sealed class TrafficMonitorMiddleware(
    RequestDelegate next,
    TrafficMonitor monitor)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var host = (context.Request.Host.Host ?? "-").ToLowerInvariant();
        var method = context.Request.Method;
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        var clientIp = context.Connection.RemoteIpAddress?.ToString();

        // Capture settings are cached in-process by SettingsService, so this is a
        // dictionary read after the first request.
        var settings = context.RequestServices.GetRequiredService<SettingsService>();
        var captureEnabled = await settings.GetAsync(DiagnosticsSettings.CaptureEnabledKey, context.RequestAborted) == "true";
        var captureSize = DiagnosticsSettings.ParseSize(
            await settings.GetAsync(DiagnosticsSettings.CaptureSizeKey, context.RequestAborted));

        var stopwatch = Stopwatch.StartNew();
        var originalBody = context.Response.Body;
        BoundedCaptureStream? requestCapture = null;
        BoundedCaptureStream? responseCapture = null;

        monitor.StartRequest(host);
        string? error = null;
        try
        {
            // Always wrap both bodies so byte counts are exact; capture is only buffered
            // (and retained for the recent-requests view) when diagnostics capture is on.
            requestCapture = new BoundedCaptureStream(context.Request.Body, captureEnabled ? captureSize : 0);
            context.Request.Body = requestCapture;
            responseCapture = new BoundedCaptureStream(originalBody, captureEnabled ? captureSize : 0);
            context.Response.Body = responseCapture;

            await next(context);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            context.Response.Body = originalBody;

            // Both streams are always assigned above before the pipeline runs; the
            // null-forgiving operator is safe in the finally path.
            monitor.Record(new RequestSample(
                DateTimeOffset.UtcNow,
                host,
                method,
                path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                requestCapture!.TotalBytes,
                responseCapture!.TotalBytes,
                clientIp,
                error,
                requestCapture.CapturedText,
                responseCapture.CapturedText));
            monitor.EndRequest(host);
        }
    }
}

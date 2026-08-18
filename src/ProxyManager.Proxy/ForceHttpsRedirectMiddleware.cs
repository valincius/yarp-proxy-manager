using Microsoft.AspNetCore.Http;

namespace ProxyManager.Proxy;

/// <summary>
/// 301-redirects plain-HTTP requests for hosts in the <see cref="ForceHttpsIndex"/> to HTTPS.
/// Runs inside the proxy pipeline before YARP; HTTPS requests are left untouched.
/// </summary>
public sealed class ForceHttpsRedirectMiddleware(
    RequestDelegate next,
    ForceHttpsIndex index,
    int? httpsPort)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.IsHttps)
        {
            var host = context.Request.Host.Host;
            if (!string.IsNullOrEmpty(host) && index.Contains(host))
            {
                var portSuffix = httpsPort is null or 443 ? string.Empty : $":{httpsPort}";
                var location = $"https://{host}{portSuffix}{context.Request.Path}{context.Request.QueryString}";
                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                context.Response.Headers.Location = location;
                return;
            }
        }

        await next(context);
    }
}

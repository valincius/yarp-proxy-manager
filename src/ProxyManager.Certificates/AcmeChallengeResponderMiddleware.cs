using Microsoft.AspNetCore.Http;

namespace ProxyManager.Certificates;

/// <summary>
/// Serves pending HTTP-01 challenge tokens at /.well-known/acme-challenge/{token}
/// so the CA can validate ownership. Runs before routing/YARP on every port.
/// </summary>
public sealed class AcmeChallengeResponderMiddleware(
    RequestDelegate next,
    Http01ChallengeStore challengeStore)
{
    private const string ChallengePathPrefix = "/.well-known/acme-challenge";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path is not null && path.StartsWith(ChallengePathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = path[(path.LastIndexOf('/') + 1)..];
            if (challengeStore.TryGetValue(token, out var keyAuthorization))
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(keyAuthorization);
                return;
            }
        }

        await next(context);
    }
}

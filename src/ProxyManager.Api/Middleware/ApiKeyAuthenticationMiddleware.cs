using System.Security.Claims;
using ProxyManager.Application.ApiKeys;

namespace ProxyManager.Api.Middleware;

/// <summary>
/// Authenticates requests that carry an API key (<c>X-Api-Key</c> header or
/// <c>Authorization: Bearer</c>). On success the principal is assigned the
/// <c>ApiKey</c> role — proxy-entity endpoints accept it, while admin-only
/// endpoints (users, backup, API keys) require the <c>Admin</c> role.
/// Runs before cookie authentication so an absent cookie leaves the principal intact.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var presented = ExtractKey(context.Request);
        if (!string.IsNullOrEmpty(presented))
        {
            var service = context.RequestServices.GetRequiredService<ApiKeyService>();
            var apiKey = await service.ValidateAsync(presented, context.RequestAborted);
            if (apiKey is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    status = 401,
                    title = "Invalid API key.",
                });
                return;
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, apiKey.Id.ToString()),
                    new Claim(ClaimTypes.Name, apiKey.Name),
                    new Claim(ClaimTypes.Role, "ApiKey"),
                ],
                "ApiKey");

            context.User = new ClaimsPrincipal(identity);
            context.Items[ApiKeyAuthenticatedKey] = true;
        }

        await next(context);
    }

    /// <summary>Set when the request was authenticated with a valid API key.</summary>
    public const string ApiKeyAuthenticatedKey = "ApiKeyAuthenticated";

    private static string? ExtractKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader)
            && !string.IsNullOrWhiteSpace(apiKeyHeader.ToString()))
        {
            return apiKeyHeader.ToString();
        }

        if (request.Headers.TryGetValue("Authorization", out var authorization)
            && authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization.ToString()["Bearer ".Length..].Trim();
        }

        return null;
    }
}

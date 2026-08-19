using Microsoft.AspNetCore.Http;

namespace ProxyManager.Proxy;

/// <summary>301/302-redirects requests for configured redirect hosts. Runs before YARP.</summary>
public sealed class RedirectMiddleware(
    RequestDelegate next,
    RedirectIndex index)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        if (!string.IsNullOrEmpty(host) && index.TryMatch(host, out var redirect))
        {
            var location = $"{redirect.Scheme}://{redirect.Host}:{redirect.Port}";
            if (redirect.PreservePath)
            {
                location += context.Request.Path + context.Request.QueryString;
            }

            context.Response.StatusCode = redirect.StatusCode;
            context.Response.Headers.Location = location;
            return;
        }

        await next(context);
    }
}

/// <summary>Enforces per-host access lists (allow/deny rules on the remote IP).</summary>
public sealed class AccessListMiddleware(
    RequestDelegate next,
    HostPolicyIndex index)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        if (!string.IsNullOrEmpty(host)
            && index.TryGet(host, out var policy)
            && policy.AccessList is { } accessList
            && !AccessListPolicyEvaluator.IsAllowed(accessList, context.Connection.RemoteIpAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}

/// <summary>Rejects requests matching common exploit patterns for hosts with exploit blocking enabled.</summary>
public sealed class ExploitBlockMiddleware(
    RequestDelegate next,
    HostPolicyIndex index)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        if (!string.IsNullOrEmpty(host)
            && index.TryGet(host, out var policy)
            && policy.BlockExploits
            && ExploitPatterns.IsSuspicious(context.Request.Path + context.Request.QueryString))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}

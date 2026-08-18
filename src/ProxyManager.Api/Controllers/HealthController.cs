using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yarp.ReverseProxy.Configuration;

namespace ProxyManager.Api.Controllers;

[Route("api/v1/health")]
public sealed class HealthController(IProxyConfigProvider proxyConfig) : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var config = proxyConfig.GetConfig();
        return Ok(new
        {
            routes = config.Routes.Count,
            clusters = config.Clusters.Count,
            checkedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Unauthenticated liveness probe for Docker HEALTHCHECK.</summary>
    [HttpGet("/healthz")]
    [AllowAnonymous]
    public IActionResult Healthz() => Ok(new { status = "healthy" });
}

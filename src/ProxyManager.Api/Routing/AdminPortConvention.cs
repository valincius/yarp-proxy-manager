using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace ProxyManager.Api.Routing;

/// <summary>
/// Marker metadata attached to every controller endpoint. The port-gate middleware in
/// Program.cs unmatchs endpoints carrying this marker when the request did not arrive on
/// the admin port (port 0 = test server), so unmatched requests fall through to the proxy
/// pipeline and proxied hosts may legitimately serve /api paths.
/// </summary>
public sealed class RequireAdminPortMetadata;

/// <summary>Attaches <see cref="RequireAdminPortMetadata"/> to every controller action endpoint.</summary>
public sealed class RequireAdminPortConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        foreach (var selector in action.Selectors)
        {
            selector.EndpointMetadata.Add(new RequireAdminPortMetadata());
        }
    }
}

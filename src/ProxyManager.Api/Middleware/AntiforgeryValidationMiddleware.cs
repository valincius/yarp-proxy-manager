using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace ProxyManager.Api.Middleware;

/// <summary>
/// Validates the antiforgery token (header X-XSRF-TOKEN) on mutating /api/v1 requests.
/// Implemented as middleware rather than MVC attributes so it works across .NET versions
/// without depending on MVC antiforgery filter registration.
/// </summary>
public sealed class AntiforgeryValidationMiddleware(
    RequestDelegate next,
    IAntiforgery antiforgery)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var isUnsafe = HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);

        if (isUnsafe && context.Request.Path.StartsWithSegments("/api/v1"))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid or missing antiforgery token.",
                });
                return;
            }
        }

        await next(context);
    }
}

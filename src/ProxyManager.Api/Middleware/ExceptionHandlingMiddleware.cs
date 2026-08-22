using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Exceptions;

namespace ProxyManager.Api.Middleware;

/// <summary>Maps application exceptions to RFC 7807 ProblemDetails responses.</summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Validation failed",
                ex.Errors.Select(e => e.ErrorMessage).ToArray());
        }
        catch (DomainConflictException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, ex.Message);
        }
        catch (AcmeOperationException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception during request {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string[]? details = null)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
        };
        if (details is { Length: > 0 })
        {
            problem.Extensions["errors"] = details;
        }

        await context.Response.WriteAsJsonAsync(problem);
    }
}

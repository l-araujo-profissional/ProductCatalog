using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductCatalog.Api.Domain;

namespace ProductCatalog.Api.Middleware;

/// <summary>
/// Central error handling: translates exceptions thrown anywhere in the pipeline into
/// RFC 7807 <c>application/problem+json</c> responses, so controllers need no try/catch blocks.
/// </summary>
/// <param name="logger">Used to log unexpected (5xx) failures.</param>
/// <param name="problemDetails">Framework service that writes the ProblemDetails body.</param>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails) : IExceptionHandler
{
    /// <summary>Maps the exception to a status code and writes the error response.</summary>
    /// <param name="httpContext">Current request context.</param>
    /// <param name="exception">Exception that escaped the request pipeline.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns><c>true</c> when the response was written (always, as this handler covers every exception).</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        int status;
        string title;
        string? detail = null;

        switch (exception)
        {
            // Expected business errors: safe to show their message to the client.
            case AppException app:
                status = app.StatusCode;
                title = app.Title;
                detail = app.Message;
                break;

            // Another request changed the same row first (optimistic concurrency).
            case DbUpdateConcurrencyException:
                status = StatusCodes.Status409Conflict;
                title = "Concurrency conflict";
                detail = "The product was modified by another request. Reload it and try again.";
                break;

            // Anything else is a bug or an infrastructure failure: log it and hide the internals.
            default:
                status = StatusCodes.Status500InternalServerError;
                title = "An unexpected error occurred";
                logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                    httpContext.Request.Method, httpContext.Request.Path);
                break;
        }

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}

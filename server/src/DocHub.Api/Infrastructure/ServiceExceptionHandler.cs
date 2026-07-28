using DocHub.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Infrastructure;

/// <summary>
/// Translates domain exceptions into RFC 7807 responses.
///
/// This is the only place that knows the mapping, which is what lets services
/// throw plain domain exceptions without ever referencing HTTP status codes.
/// </summary>
internal sealed class ServiceExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ServiceExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException notFound =>
                (StatusCodes.Status404NotFound, "Not found", notFound.Message),

            ValidationException validation =>
                (StatusCodes.Status400BadRequest, "Invalid request", validation.Message),

            // Anything else is a bug or an outage: log it in full, but never
            // leak internals to the caller.
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error",
                "Something went wrong handling this request."),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception on {Path}", context.Request.Path);
        else
            logger.LogInformation("{Title} on {Path}: {Detail}", title, context.Request.Path, detail);

        context.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Type = $"https://httpstatuses.io/{status}",
            },
        });
    }
}

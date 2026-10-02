using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Api.Middleware;

// Single place that maps exceptions to HTTP (coding rules #35-#37). Responses are RFC 7807 problem details with a
// traceId and never contain stack traces, SQL or other internal details.
public sealed class GlobalExceptionHandler(
    IDatabaseErrorClassifier databaseErrors,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return false;
        }

        var problem = Map(exception);
        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception. TraceId {TraceId}", httpContext.TraceIdentifier);
        }

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        // Serialized by its runtime type, otherwise ValidationProblemDetails would lose its `errors`.
        await httpContext.Response.WriteAsJsonAsync(
            problem, problem.GetType(), options: null, contentType: "application/problem+json", cancellationToken);

        return true;
    }

    private ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException validation => new ValidationProblemDetails(
            validation.Errors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        },
        AuthenticationFailedException authentication => Problem(StatusCodes.Status401Unauthorized, "Authentication failed.", authentication.Message),
        ForbiddenException forbidden => Problem(StatusCodes.Status403Forbidden, "Forbidden.", forbidden.Message),
        NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "Not found.", notFound.Message),
        ConflictException conflict => Problem(StatusCodes.Status409Conflict, "Conflict.", conflict.Message),
        DbUpdateConcurrencyException => Problem(
            StatusCodes.Status409Conflict, "Conflict.", "The record was changed by someone else. Reload and try again."),
        DbUpdateException update when databaseErrors.IsUniqueViolation(update) => Problem(
            StatusCodes.Status409Conflict, "Conflict.", "A record with the same unique value already exists."),
        BusinessRuleException rule => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated.", rule.Message),
        DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated.", domain.Message),
        _ => Problem(StatusCodes.Status500InternalServerError, "Server error.", "An unexpected error occurred.")
    };

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}

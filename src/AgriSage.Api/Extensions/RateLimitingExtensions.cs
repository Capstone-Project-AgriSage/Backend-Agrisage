using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Extensions;

public static class RateLimitingExtensions
{
    public const string AuthPolicy = "auth";

    // Public catalog reads: generous, only to slow down scraping.
    public const string PublicPolicy = "public";

    // Limits register/login attempts per client IP (password guessing, account spam). Fixed window, no queue.
    // Behind a proxy the forwarded client IP must be configured at deployment, otherwise all clients share one bucket.
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            options.AddPolicy(PublicPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Detail = "Too many attempts. Try again later."
                };
                problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    problem, options: null, contentType: "application/problem+json", cancellationToken);
            };
        });

        return services;
    }
}

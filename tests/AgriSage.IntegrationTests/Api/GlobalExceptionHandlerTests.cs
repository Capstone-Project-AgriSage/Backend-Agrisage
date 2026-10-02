using System.Text.Json;
using AgriSage.Api.Middleware;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgriSage.IntegrationTests.Api;

public class GlobalExceptionHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<Exception, int> Mappings => new()
    {
        { new ValidationException(new Dictionary<string, string[]> { ["email"] = ["bad"] }), 400 },
        { new AuthenticationFailedException(), 401 },
        { new ForbiddenException(), 403 },
        { new NotFoundException("Order", 1), 404 },
        { new ConflictException("duplicate"), 409 },
        { new DbUpdateConcurrencyException("stale"), 409 },
        { new BusinessRuleException("rule"), 422 },
        { new DomainException("invariant"), 422 },
        { new StorageUnavailableException(), 503 },
        { new InvalidOperationException("boom"), 500 }
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task Exceptions_map_to_the_documented_status(Exception exception, int status)
    {
        var (handled, context) = await HandleAsync(exception, uniqueViolation: false);

        Assert.True(handled);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.False(string.IsNullOrEmpty(ReadBody(context).GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Unique_violation_becomes_a_conflict_and_other_database_errors_stay_500()
    {
        var update = new DbUpdateException("insert failed", new Exception("duplicate key value violates unique constraint"));

        var (_, conflict) = await HandleAsync(update, uniqueViolation: true);
        var (_, error) = await HandleAsync(update, uniqueViolation: false);

        Assert.Equal(409, conflict.Response.StatusCode);
        Assert.Equal(500, error.Response.StatusCode);
    }

    [Fact]
    public async Task Server_errors_never_expose_exception_details()
    {
        var secret = new InvalidOperationException("Host=db.example.com;Password=hunter2 at C:\\src\\Internal.cs");

        var (_, context) = await HandleAsync(secret, uniqueViolation: false);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(Token);

        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("Internal.cs", body);
        Assert.DoesNotContain("db.example.com", body);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validation_errors_are_returned_per_field()
    {
        var exception = new ValidationException(new Dictionary<string, string[]> { ["password"] = ["too short"] });

        var (_, context) = await HandleAsync(exception, uniqueViolation: false);

        var errors = ReadBody(context).GetProperty("errors").GetProperty("password");
        Assert.Equal("too short", errors[0].GetString());
    }

    [Fact]
    public async Task Cancelled_requests_are_not_handled()
    {
        var (handled, _) = await HandleAsync(new OperationCanceledException(), uniqueViolation: false);

        Assert.False(handled);
    }

    private static async Task<(bool Handled, DefaultHttpContext Context)> HandleAsync(Exception exception, bool uniqueViolation)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            TraceIdentifier = "trace-1"
        };
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(new FakeClassifier(uniqueViolation), NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, Token);

        return (handled, context);
    }

    private static JsonElement ReadBody(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement.Clone();
    }

    private sealed class FakeClassifier(bool uniqueViolation) : IDatabaseErrorClassifier
    {
        public bool IsUniqueViolation(DbUpdateException exception) => uniqueViolation;
    }
}

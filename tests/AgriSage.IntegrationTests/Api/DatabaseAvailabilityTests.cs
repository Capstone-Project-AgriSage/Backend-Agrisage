using System.Net;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace AgriSage.IntegrationTests.Api;

public sealed class DatabaseAvailabilityTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [Theory]
    [InlineData("XX000", "(EMAXCONNSESSION) max clients reached in session mode - pool_size: 15", true)]
    [InlineData("XX000", "Max clients reached", true)]
    [InlineData("53300", "too many connections", true)]
    [InlineData("57P03", "database is starting up", true)]
    [InlineData("08006", "connection failure", true)]
    [InlineData("XX000", "unrelated internal error", false)]
    [InlineData("23505", "duplicate key value", false)]
    [InlineData("42601", "syntax error", false)]
    public void Connection_capacity_is_recognized_without_masking_other_postgres_failures(string state, string message, bool expected)
    {
        var classifier = new NpgsqlErrorClassifier();
        var error = new PostgresException(message, "FATAL", "FATAL", state);
        Assert.Equal(expected, classifier.IsUnavailable(error));
        Assert.Equal(expected, classifier.IsUnavailable(new DbUpdateException("write failed", error)));
    }

    [Fact]
    public void Pool_timeout_is_unavailable_but_an_unrelated_application_timeout_is_not()
    {
        var classifier = new NpgsqlErrorClassifier();
        Assert.True(classifier.IsUnavailable(new NpgsqlException("pool exhausted", new TimeoutException())));
        Assert.False(classifier.IsUnavailable(new TimeoutException("other service")));
    }

    [Fact]
    public async Task Database_capacity_during_jwt_validation_is_503_not_401_and_never_exposes_provider_details()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserAccessValidator>();
            services.AddSingleton<IUserAccessValidator, BusyDatabaseValidator>();
        }));
        using var client = app.CreateClient();
        var jwt = app.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), "SALES_STAFF");
        client.DefaultRequestHeaders.Authorization = new("Bearer", jwt.Value);
        var response = await client.GetAsync("/api/me/permissions", token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
        var body = await response.Content.ReadAsStringAsync(token);
        Assert.Contains("Database temporarily unavailable", body);
        Assert.DoesNotContain("EMAXCONNSESSION", body);
        Assert.DoesNotContain("pool_size", body);
        Assert.DoesNotContain("An unexpected error", body);
    }

    private sealed class BusyDatabaseValidator : IUserAccessValidator
    {
        public Task<bool> IsActiveAsync(Guid userId, CancellationToken token) => throw Busy();
        public Task<bool> IsSessionAllowedAsync(Guid userId, Guid sessionId, long version, string role, CancellationToken token) => throw Busy();
        private static Exception Busy() => new PostgresException("(EMAXCONNSESSION) max clients reached in session mode - pool_size: 15", "FATAL", "FATAL", "XX000");
    }
}

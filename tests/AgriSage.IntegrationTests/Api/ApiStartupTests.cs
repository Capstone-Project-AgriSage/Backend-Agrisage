using System.Net;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

public class ApiStartupTests : IClassFixture<ApiStartupTests.DevelopmentApiFactory>
{
    private readonly DevelopmentApiFactory _factory;

    public ApiStartupTests(DevelopmentApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Api_starts_and_serves_swagger_document_in_development()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Date_time_provider_is_registered()
    {
        var dateTimeProvider = _factory.Services.GetRequiredService<IDateTimeProvider>();

        Assert.Equal(TimeSpan.Zero, dateTimeProvider.UtcNow.Offset);
    }

    [Fact]
    public void Persistence_abstraction_resolves_to_the_scoped_db_context()
    {
        using var scope = _factory.Services.CreateScope();

        var abstraction = scope.ServiceProvider.GetRequiredService<IAgriSageDbContext>();

        Assert.Same(scope.ServiceProvider.GetRequiredService<AgriSageDbContext>(), abstraction);
    }

    [Fact]
    public void Db_context_runs_the_persistence_interceptors_in_order()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();

        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!;

        Assert.Collection(
            interceptors,
            interceptor => Assert.IsType<SoftDeleteInterceptor>(interceptor),
            interceptor => Assert.IsType<AuditableEntityInterceptor>(interceptor),
            interceptor => Assert.IsType<ConcurrencyVersionInterceptor>(interceptor));
    }

    [Fact]
    public void Current_user_outside_a_request_is_anonymous()
    {
        using var scope = _factory.Services.CreateScope();

        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
    }

    public sealed class DevelopmentApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // Test-only key; real keys come from User Secrets / environment variables.
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-a-secret-000000");
        }
    }
}

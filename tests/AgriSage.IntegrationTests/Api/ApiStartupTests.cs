using System.Net;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Files;
using AgriSage.Application.Features.Staff.Dtos.Requests;
using AgriSage.Application.Features.Staff.Interfaces;
using AgriSage.Application.Features.Staff.Services;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    public void Auth_services_are_registered()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        Assert.IsType<AuthService>(provider.GetRequiredService<IAuthService>());
        Assert.IsType<AdminBootstrapService>(provider.GetRequiredService<IAdminBootstrapService>());
        Assert.IsType<ProductImageService>(provider.GetRequiredService<IProductImageService>());
        Assert.IsType<AgriSage.Infrastructure.Storage.SupabaseFileStorageService>(provider.GetRequiredService<IFileStorageService>());
        Assert.IsType<StaffService>(provider.GetRequiredService<IStaffService>());
        Assert.IsType<UserAccessValidator>(provider.GetRequiredService<IUserAccessValidator>());
        Assert.NotNull(provider.GetRequiredService<FluentValidation.IValidator<CreateStaffRequest>>());
        Assert.NotNull(provider.GetRequiredService<FluentValidation.IValidator<StaffListRequest>>());
        Assert.NotNull(provider.GetRequiredService<IPasswordHashService>());
        Assert.NotNull(provider.GetRequiredService<IAccessTokenService>());
        Assert.NotNull(provider.GetRequiredService<IDatabaseErrorClassifier>());
        Assert.NotNull(provider.GetRequiredService<FluentValidation.IValidator<RegisterFarmerRequest>>());
        Assert.NotNull(provider.GetRequiredService<FluentValidation.IValidator<LoginRequest>>());
    }

    [Fact]
    public void Current_user_outside_a_request_is_anonymous()
    {
        using var scope = _factory.Services.CreateScope();

        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void Reference_seeder_and_approved_development_store_options_resolve_without_seeding()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<DatabaseSeeder>(),
            scope.ServiceProvider.GetRequiredService<DatabaseSeeder>());
        var store = scope.ServiceProvider.GetRequiredService<IOptions<SeedStoreOptions>>().Value;
        store.Validate();
        Assert.Equal("AGRISAGE-DEV", store.Code);
        Assert.Equal("AgriSage Dev Store", store.Name);
        Assert.Equal("Dev address - to be replaced", store.AddressLine);
        Assert.Equal("Can Tho", store.Province);
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

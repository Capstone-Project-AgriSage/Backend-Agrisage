using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.Infrastructure.Persistence.Seed;
using AgriSage.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<ConcurrencyVersionInterceptor>();

        // Non-secret "Database" settings come from appsettings; only Database:Password is a secret
        // (User Secrets / Database__Password). Nothing connects at startup.
        var connectionString = (configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? new DatabaseOptions()).BuildConnectionString();

        services.AddDbContext<AgriSageDbContext>((provider, options) =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql();
            }
            else
            {
                options.UseNpgsql(connectionString);
            }

            // Order matters (database design §35.15): soft delete → timestamps → version.
            options.AddInterceptors(
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditableEntityInterceptor>(),
                provider.GetRequiredService<ConcurrencyVersionInterceptor>());
        });

        services.AddScoped<IAgriSageDbContext>(provider => provider.GetRequiredService<AgriSageDbContext>());

        services.AddOptions<SeedStoreOptions>().Bind(configuration.GetSection(SeedStoreOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        // Provider adapters (payOS, AI, storage) are registered here by later tasks.

        return services;
    }
}

using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.GoodsReceipts.Import;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.Infrastructure.Persistence.Seed;
using AgriSage.Infrastructure.Services;
using AgriSage.Infrastructure.Spreadsheets;
using AgriSage.Infrastructure.Storage;
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
        services.AddSingleton<IPasswordHashService, PasswordHashService>();
        services.AddSingleton<IAccessTokenService, AccessTokenService>();
        services.AddSingleton<IDatabaseErrorClassifier, NpgsqlErrorClassifier>();

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
        services.AddScoped<IRowLockService, RowLockService>();

        services.AddOptions<SeedStoreOptions>().Bind(configuration.GetSection(SeedStoreOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        // Object storage (Supabase); nothing connects at startup and a missing configuration fails only on use.
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));
        services.AddHttpClient<IFileStorageService, SupabaseFileStorageService>(client => client.Timeout = TimeSpan.FromSeconds(30));

        // Goods receipt Excel template (ClosedXML, stateless).
        services.AddSingleton<IReceiptSpreadsheet, ClosedXmlReceiptSpreadsheet>();

        // Further provider adapters (payOS, AI) are registered here by later tasks.

        return services;
    }
}

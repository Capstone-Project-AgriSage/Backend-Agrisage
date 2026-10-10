using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.GoodsReceipts.Import;
using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Notifications;
using AgriSage.Infrastructure.Ai;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.BackgroundJobs;
using AgriSage.Infrastructure.Messaging;
using AgriSage.Infrastructure.Payments;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.Infrastructure.Persistence.Seed;
using AgriSage.Infrastructure.Services;
using AgriSage.Infrastructure.Spreadsheets;
using AgriSage.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton<ISecretTokenService, SecretTokenService>();
        services.AddScoped<IAuthSecurityLock, AuthSecurityLock>();
        services.AddScoped<IAuditRequestMetadata, AuditRequestMetadata>();
        services.AddScoped<INotificationOutboxLock, NotificationOutboxLock>();
        services.AddScoped<IBackgroundJobLock, PostgresBackgroundJobLock>();
        services.AddOptions<AuthSecurityOptions>().Bind(configuration.GetSection("AuthSecurity"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<MessageDeliveryOptions>().Bind(configuration.GetSection("MessageDelivery"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddHttpClient<IAuthMessageSender, AuthMessageSender>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddOptions<BackgroundJobOptions>().Bind(configuration.GetSection("BackgroundJobs"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddHostedService<OperationsWorker>();
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
        services.AddScoped<AgriSage.Application.Features.Permissions.IPermissionWriteLock, PermissionWriteLock>();
        services.AddScoped<ICustomerGroupDefaultSwitcher, CustomerGroupDefaultSwitcher>();
        services.AddScoped<IUserAddressDefaultSwitcher, UserAddressDefaultSwitcher>();

        services.AddOptions<SeedStoreOptions>().Bind(configuration.GetSection(SeedStoreOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        // Object storage (Supabase); nothing connects at startup and a missing configuration fails only on use.
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));
        services.AddHttpClient<IFileStorageService, SupabaseFileStorageService>(client => client.Timeout = TimeSpan.FromSeconds(30));

        // payOS (F2.4): a missing configuration only makes the payOS endpoints answer 503.
        services.AddOptions<PayOsOptions>().Bind(configuration.GetSection(PayOsOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        if (PayOsMode.IsSimulated(configuration))
        {
            // Test environment only (Program refuses this outside Development): no payOS call, the web app gets a test button.
            services.AddSingleton<SimulatedPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(provider => provider.GetRequiredService<SimulatedPaymentGateway>());
            services.AddSingleton<ISimulatedPaymentGateway>(provider => provider.GetRequiredService<SimulatedPaymentGateway>());
        }
        else
        {
            services.AddHttpClient<IPaymentGateway, PayOsPaymentGateway>(client => client.Timeout = TimeSpan.FromSeconds(30));
        }

        // Goods receipt Excel template (ClosedXML, stateless).
        services.AddSingleton<IReceiptSpreadsheet, ClosedXmlReceiptSpreadsheet>();

        // Objects in the private diagnosis bucket (signed URLs, bytes): the same typed client as the public bucket.
        services.AddTransient<IPrivateFileStore>(provider => (IPrivateFileStore)provider.GetRequiredService<IFileStorageService>());

        // AI inference service (FastAPI). A missing configuration only makes diagnosis cases FAILED, never the API start.
        services.AddOptions<AiServiceOptions>().Bind(configuration.GetSection(AiServiceOptions.SectionName));
        if (AiServiceMode.IsSimulated(configuration))
        {
            // Development only (Program refuses this elsewhere): no call, deterministic made-up predictions.
            services.AddSingleton<IAiDiagnosisClient, SimulatedDiagnosisClient>();
        }
        else
        {
            services.AddHttpClient<IAiDiagnosisClient, FastApiDiagnosisClient>(client => client.Timeout = TimeSpan.FromSeconds(130));
        }

        return services;
    }
}

using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();

        // Further feature application services are registered here by later tasks.

        return services;
    }
}

using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Feature application services are registered here by later tasks.

        return services;
    }
}

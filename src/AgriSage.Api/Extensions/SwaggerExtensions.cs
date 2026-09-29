using Microsoft.OpenApi;

namespace AgriSage.Api.Extensions;

public static class SwaggerExtensions
{
    private const string BearerSchemeId = "Bearer";

    public static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "AgriSage API",
                Version = "v1"
            });

            options.AddSecurityDefinition(BearerSchemeId, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the JWT access token (without the 'Bearer ' prefix)."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeId, document)] = []
            });
        });

        return services;
    }

    public static WebApplication UseApiSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "AgriSage API v1");
        });

        return app;
    }
}

using System.Text;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgriSage.Api.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Configured from validated JwtOptions (bound in Infrastructure) instead of reading raw configuration here.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = AgriSageClaimTypes.Subject,
                    RoleClaimType = AgriSageClaimTypes.Role,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                bearer.Events = new JwtBearerEvents { OnTokenValidated = RejectInactiveAccountAsync };
            });

        return services;
    }

    // A valid signature is not enough: a locked, deleted or unknown account must stop working immediately,
    // not when its (up to 60 minute) access token expires.
    private static async Task RejectInactiveAccountAsync(TokenValidatedContext context)
    {
        var subject = context.Principal?.FindFirst(AgriSageClaimTypes.Subject)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            context.Fail("The token has no valid subject.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IUserAccessValidator>();
        if (!await validator.IsActiveAsync(userId, context.HttpContext.RequestAborted))
        {
            context.Fail("The account is not active.");
        }
    }
}

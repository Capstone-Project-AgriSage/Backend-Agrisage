using System.Security.Claims;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence.Conventions;
using AgriSage.IntegrationTests.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

public class AuthInfrastructureTests : IClassFixture<ApiStartupTests.DevelopmentApiFactory>
{
    private readonly ApiStartupTests.DevelopmentApiFactory _factory;

    public AuthInfrastructureTests(ApiStartupTests.DevelopmentApiFactory factory)
    {
        _factory = factory;
    }

    // ----- Password hashing -----

    [Fact]
    public void Password_hash_is_salted_and_verifies_only_the_right_password()
    {
        var hasher = new PasswordHashService();

        var first = hasher.Hash("correct horse");
        var second = hasher.Hash("correct horse");

        Assert.NotEqual("correct horse", first);
        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerification.Success, hasher.Verify(first, "correct horse"));
        Assert.Equal(PasswordVerification.Failed, hasher.Verify(first, "wrong horse"));
    }

    [Fact]
    public void Unknown_account_verification_fails_without_a_hash()
    {
        Assert.Equal(PasswordVerification.Failed, new PasswordHashService().Verify(null, "anything"));
    }

    // ----- Access token -----

    [Fact]
    public async Task Issued_token_passes_the_api_validation_with_sub_role_and_expiry()
    {
        var userId = Guid.NewGuid();
        var token = _factory.Services.GetRequiredService<IAccessTokenService>().Issue(userId, "FARMER");

        var result = await ValidateAsync(token.Value);

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(userId.ToString(), result.ClaimsIdentity.FindFirst(AgriSageClaimTypes.Subject)?.Value);
        Assert.Equal("FARMER", result.ClaimsIdentity.FindFirst(AgriSageClaimTypes.Role)?.Value);
        Assert.NotNull(result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Jti));
        // The lifetime is configuration (60 minutes by default, a day in Development for the mobile apps), so read it.
        var minutes = _factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.AccessTokenMinutes;
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(minutes - 1) && token.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(minutes + 1));
        Assert.True(result.ClaimsIdentity.IsAuthenticated);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var options = _factory.Services.GetRequiredService<IOptions<JwtOptions>>();
        // Issued long enough ago to be expired whatever the configured lifetime is (plus the 30 s of clock skew).
        var service = new AccessTokenService(options, new FixedClock(DateTimeOffset.UtcNow.AddMinutes(-(options.Value.AccessTokenMinutes + 60))));

        var result = await ValidateAsync(service.Issue(Guid.NewGuid(), "FARMER").Value);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var other = new AccessTokenService(
            Options.Create(new JwtOptions
            {
                Issuer = "AgriSage",
                Audience = "AgriSage.Clients",
                SigningKey = "another-signing-key-that-is-long-enough-000000",
                AccessTokenMinutes = 60
            }),
            new FixedClock(DateTimeOffset.UtcNow));

        var result = await ValidateAsync(other.Issue(Guid.NewGuid(), "ADMIN").Value);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Current_user_service_reads_the_user_id_from_a_validated_token()
    {
        var userId = Guid.NewGuid();
        var token = _factory.Services.GetRequiredService<IAccessTokenService>().Issue(userId, "SALES_STAFF");
        var validated = await ValidateAsync(token.Value);
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(validated.ClaimsIdentity) }
        };

        var currentUser = new CurrentUserService(accessor);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(userId, currentUser.UserId);
        Assert.Equal("SALES_STAFF", ((ICurrentUserService)currentUser).Role);
    }

    [Fact]
    public void Current_user_has_no_role_without_a_request()
    {
        ICurrentUserService currentUser = new CurrentUserService(new HttpContextAccessor());

        Assert.Null(currentUser.Role);
    }

    [Fact]
    public void Role_code_text_equals_the_database_enum_value_for_every_role()
    {
        foreach (var role in Enum.GetValues<RoleCode>())
        {
            Assert.Equal(EnumDbValue.ToDb(role), RoleCodeFormat.ToText(role));
        }
    }

    private async Task<Microsoft.IdentityModel.Tokens.TokenValidationResult> ValidateAsync(string token)
    {
        var bearer = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        return await new JsonWebTokenHandler().ValidateTokenAsync(token, bearer.TokenValidationParameters);
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}

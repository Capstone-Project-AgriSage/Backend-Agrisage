using System.Security.Claims;
using AgriSage.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

public class CurrentUserServiceTests
{
    [Fact]
    public void Without_a_request_there_is_no_current_user()
    {
        var service = new CurrentUserService(new HttpContextAccessor());

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.UserId);
    }

    [Fact]
    public void Anonymous_request_has_no_current_user()
    {
        var service = CreateService(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]));

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.UserId);
    }

    [Fact]
    public void Authenticated_user_id_comes_from_the_sub_claim()
    {
        var userId = Guid.NewGuid();
        var service = CreateService(new ClaimsIdentity([new Claim("sub", userId.ToString())], "Bearer"));

        Assert.True(service.IsAuthenticated);
        Assert.Equal(userId, service.UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public void Authenticated_user_without_a_valid_sub_claim_is_an_error(string? subject)
    {
        Claim[] claims = subject is null ? [] : [new Claim("sub", subject)];
        var service = CreateService(new ClaimsIdentity(claims, "Bearer"));

        Assert.Throws<InvalidOperationException>(() => service.UserId);
    }

    private static CurrentUserService CreateService(ClaimsIdentity identity) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } });
}

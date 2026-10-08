using System.Text.Json;

namespace AgriSage.UnitTests.Architecture;

// The mobile apps have no refresh token, so the access token has to last: 1440 minutes (the maximum JwtOptions accepts)
// in Development, while the shared default stays at 60.
public class TokenLifetimeConfigurationTests
{
    private static JsonElement Load(string file)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "AgriSage.sln"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, "src", "AgriSage.Api", file)));
        return document.RootElement.Clone();
    }

    [Fact]
    public void The_shared_settings_keep_a_one_hour_token()
    {
        var jwt = Load("appsettings.json").GetProperty("Jwt");

        Assert.Equal(60, jwt.GetProperty("AccessTokenMinutes").GetInt32());
        Assert.Equal("AgriSage", jwt.GetProperty("Issuer").GetString());
        Assert.Equal("AgriSage.Clients", jwt.GetProperty("Audience").GetString());
    }

    [Fact]
    public void Development_lets_a_session_last_a_day_within_the_range_the_options_accept()
    {
        var jwt = Load("appsettings.Development.json").GetProperty("Jwt");

        var minutes = jwt.GetProperty("AccessTokenMinutes").GetInt32();
        Assert.Equal(1440, minutes);
        Assert.InRange(minutes, 1, 1440); // JwtOptions: [Range(1, 1440)]
        // Only the lifetime is overridden: the issuer, audience and signing key keep coming from the shared settings and secrets.
        Assert.False(jwt.TryGetProperty("SigningKey", out _));
        Assert.False(jwt.TryGetProperty("Issuer", out _));
    }
}

using System.Net;

namespace AgriSage.IntegrationTests.Api;

// GET /health is the liveness check of a hosting platform (staging): anonymous, plain text, no database call.
public sealed class HealthHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [Fact]
    public async Task Health_answers_anonymously_without_the_database()
    {
        var token = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(token));
    }
}

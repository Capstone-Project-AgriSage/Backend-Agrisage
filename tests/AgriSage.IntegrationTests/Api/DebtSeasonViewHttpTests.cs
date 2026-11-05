using System.Net;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

// F3.17 filters: validated before any database access, on the staff and the Farmer routes.
public sealed class DebtSeasonViewHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }

    [Theory]
    [InlineData("ADMIN", "/api/debt-entries?dueState=BOGUS")]
    [InlineData("ADMIN", "/api/debt-entries?originSeasonId=00000000-0000-0000-0000-000000000000")]
    [InlineData("ADMIN", "/api/debt-entries?collectionSeasonId=00000000-0000-0000-0000-000000000000")]
    [InlineData("FARMER", "/api/me/debt-entries?dueState=LATE")]
    public async Task Unknown_filter_values_are_rejected(string role, string url)
    {
        using var c = Client(role);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url, Token)).StatusCode);
    }
}

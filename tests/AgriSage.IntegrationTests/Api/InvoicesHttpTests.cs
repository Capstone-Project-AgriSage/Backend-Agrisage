using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

public class InvoicesHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return client;
    }
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("FARMER", HttpStatusCode.Forbidden)]
    [InlineData("DELIVERY_STAFF", HttpStatusCode.Forbidden)]
    public async Task Invoice_files_and_history_are_private(string? role, HttpStatusCode expected)
    {
        using var client = Client(role); var id = Guid.NewGuid();
        foreach (var path in new[] { "/api/invoices", $"/api/invoices/{id}", $"/api/invoices/{id}/pdf", $"/api/invoices/{id}/emails" })
            Assert.Equal(expected, (await client.GetAsync(path, Token)).StatusCode);
        Assert.Equal(expected, (await client.PostAsync($"/api/orders/{id}/invoice", null, Token)).StatusCode);
    }
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("STORE_OWNER")]
    [InlineData("SALES_STAFF")]
    public async Task Authorized_staff_still_pass_paging_and_email_validation(string role)
    {
        using var client = Client(role);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/invoices?pageSize=101", Token)).StatusCode);
        foreach (var query in new[] { "fromDate=2026-11-24&toDate=2026-11-23", "fromDate=0001-01-01", "toDate=9999-12-31",
            "minAmount=-1", "maxAmount=0.001", "minAmount=200&maxAmount=100", "emailStatus=PAID", "hasCustomerEmail=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/invoices?" + query, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/invoices/{Guid.NewGuid()}/emails?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/invoices/{Guid.NewGuid()}/emails", new { recipient = "invalid", requestId = Guid.Empty }, Token)).StatusCode);
    }
}

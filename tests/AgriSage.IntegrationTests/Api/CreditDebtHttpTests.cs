using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Api;

public sealed class CreditDebtHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string Id = Guid.NewGuid().ToString();
    private HttpClient Client(string? role)
    {
        var c = factory.CreateClient();
        if (role != null) c.DefaultRequestHeaders.Authorization = new("Bearer", factory.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role).Value);
        return c;
    }
    public static TheoryData<string, string> Endpoints => new()
    {
        { "POST", $"/api/customers/{Id}/credit-eligibility" }, { "GET", "/api/debt-entries" },
        { "GET", $"/api/debt-entries/{Id}" }, { "GET", "/api/debt-accounts" },
        { "GET", $"/api/customers/{Id}/debt" }, { "GET", $"/api/customers/{Id}/debt/entries" },
        { "GET", $"/api/customers/{Id}/debt/transactions" }, { "GET", $"/api/customers/{Id}/debt/allocation-preview" },
        { "GET", $"/api/debt-entries/{Id}/payments" }, { "GET", $"/api/debt-entries/{Id}/ledger" },
        { "POST", $"/api/debt-entries/{Id}/adjust" }, { "POST", $"/api/debt-entries/{Id}/cancel" },
        { "POST", $"/api/debt-entries/{Id}/dispute" }, { "POST", $"/api/debt-entries/{Id}/keep" },
        { "POST", $"/api/debt-entries/{Id}/change-due-date" }, { "POST", $"/api/customers/{Id}/debt/manual-entries" },
        { "POST", "/api/payments/bank-transfer" }, { "POST", $"/api/payments/{Id}/confirm" }, { "POST", $"/api/payments/{Id}/reject" },
        { "GET", "/api/debt-entries/dashboard" }, { "GET", "/api/reports/debt-aging" }, { "GET", "/api/reports/debt-collections" },
        { "GET", "/api/reports/debt-by-customer-group" }
    };
    private static HttpRequestMessage Request(string method, string url) => new(new HttpMethod(method), url)
    { Content = method == "POST" ? JsonContent.Create(new { }) : null };
    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Staff_endpoints_require_auth_and_reject_farmer_and_delivery(string method, string url)
    {
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, url), Token)).StatusCode);
        foreach (var role in new[] { "FARMER", "DELIVERY_STAFF" })
        { using var c = Client(role); Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Request(method, url), Token)).StatusCode); }
    }
    [Theory]
    [InlineData("/api/payments/{id}/confirm")]
    [InlineData("/api/payments/{id}/reject")]
    [InlineData("/api/debt-entries/{id}/adjust")]
    [InlineData("/api/debt-entries/{id}/cancel")]
    [InlineData("/api/customers/{id}/debt/manual-entries")]
    public async Task Sales_cannot_review_bank_receipts_or_adjust_debt(string route)
    {
        using var c = Client("SALES_STAFF");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(route.Replace("{id}", Id), new { }, Token)).StatusCode);
    }
    [Fact]
    public async Task Invalid_money_reason_pagination_and_dates_are_rejected_before_database_access()
    {
        using var c = Client("ADMIN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/customers/{Id}/credit-eligibility", new { orderAmount = -1 }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/payments/bank-transfer", new { farmerProfileId = Id, amount = 0 }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/payments/{Id}/reject", new { reason = "" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/debt-entries?pageSize=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/debt-entries?status=BOGUS", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/reports/debt-collections?fromDate=2026-10-04&toDate=2026-10-03", Token)).StatusCode);
    }
    [Fact]
    public void Runtime_resolves_real_posting_implementations_and_new_services_once()
    {
        using var scope = factory.Services.CreateScope(); var s = scope.ServiceProvider;
        Assert.IsType<OrderSettlementGuard>(s.GetRequiredService<IOrderSettlementGuard>());
        Assert.IsType<CreditReservationAdjuster>(s.GetRequiredService<ICreditReservationAdjuster>());
        Assert.IsType<FulfillmentFinancialPosting>(s.GetRequiredService<IFulfillmentFinancialPosting>());
        Assert.IsType<DebtRepaymentPosting>(s.GetRequiredService<IDebtRepaymentPosting>());
        Assert.IsType<DebtReturnPosting>(s.GetRequiredService<IDebtReturnPosting>());
        Assert.Single(s.GetServices<ICreditEligibilityService>()); Assert.Single(s.GetServices<IDebtService>());
        Assert.Single(s.GetServices<IBankDebtPaymentService>()); Assert.Single(s.GetServices<IDebtReportService>());
    }
    [Fact]
    public async Task Swagger_has_no_duplicate_routes_and_exposes_all_debt_endpoints()
    {
        using var c = Client(null);
        var paths = JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json", Token)).RootElement.GetProperty("paths");
        foreach (var row in Endpoints)
        {
            var path = row.Data.Item2;
            Assert.True(paths.TryGetProperty(path.Replace(Id, path.Contains("customers") ? "{farmerProfileId}" : "{id}"), out _), path);
        }
        Assert.True(paths.TryGetProperty("/api/me/credit", out _)); Assert.True(paths.TryGetProperty("/api/me/debt", out _));
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgriSage.IntegrationTests.Api;

// F3.21 through the real routes (authentication, role and permission filters, centralized error handling). The permission is
// granted to the caller on purpose. Bodies are valid, so an allowed caller would reach the service; the owner-only guard is the
// first call of each service, before any database access, so no database is needed to see the refusal.
public sealed class OwnerOnlyDebtPermissionHttpTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly Guid Id = Guid.NewGuid();

    public static TheoryData<string, string, object?> Routes => new()
    {
        { "DEBT.ADJUST", $"/api/debt-entries/{Id}/adjust", new { amount = 100, reason = "r" } },
        { "DEBT.CANCEL", $"/api/debt-entries/{Id}/cancel", new { reason = "r" } },
        { "DEBT.MANUAL", $"/api/customers/{Id}/debt/manual-entries", new { amount = 100, dueDate = "2026-12-01", reason = "r" } },
        { "BANK_PAYMENTS.CONFIRM", $"/api/payments/{Id}/confirm", null },
        { "BANK_PAYMENTS.REJECT", $"/api/payments/{Id}/reject", new { reason = "r" } },
        { "CREDIT.SUSPEND", $"/api/customers/{Id}/credit/suspend", new { reason = "r" } },
        { "CREDIT.BLOCK", $"/api/customers/{Id}/credit/block", new { reason = "r" } },
        { "CREDIT.ACTIVATE", $"/api/customers/{Id}/credit/activate", new { reason = "r" } },
        { "DEBT.DUE_DATE", $"/api/debt-entries/{Id}/change-due-date", new { newDueDate = "2099-12-01", reason = "r" } },
    };

    private (HttpClient Client, IAsyncDisposable App) Client(string code, string role)
    {
        var grants = new ConcurrentDictionary<string, bool> { [code] = true };
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPermissionEvaluator>();
            services.AddSingleton<IPermissionEvaluator>(new LivePermissions(grants, role));
        }));
        var client = app.CreateClient();
        var jwt = app.Services.GetRequiredService<IAccessTokenService>().Issue(Guid.NewGuid(), role);
        client.DefaultRequestHeaders.Authorization = new("Bearer", jwt.Value);
        return (client, app);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sale_staff_with_a_granted_permission_is_still_forbidden(string code, string route, object? body)
    {
        var (client, app) = Client(code, "SALES_STAFF");
        await using var _ = app; using var __ = client;

        var response = body is null
            ? await client.PostAsync(route, null, TestContext.Current.CancellationToken)
            : await client.PostAsJsonAsync(route, body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Control: an owner passes the filters and stops at request validation (empty body), never at the permission gate.
    [Theory]
    [InlineData("DEBT.ADJUST", "adjust")]
    [InlineData("DEBT.CANCEL", "cancel")]
    [InlineData("DEBT.DUE_DATE", "change-due-date")]
    public async Task Store_owner_with_the_permission_reaches_request_validation(string code, string action)
    {
        var (client, app) = Client(code, "STORE_OWNER");
        await using var _ = app; using var __ = client;

        var response = await client.PostAsJsonAsync($"/api/debt-entries/{Id}/{action}", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class LivePermissions(ConcurrentDictionary<string, bool> grants, string role) : IPermissionEvaluator
    {
        public Task<bool> HasAsync(string code, CancellationToken token) => Task.FromResult(grants.GetValueOrDefault(code));
        public Task<CurrentPermissionsResponse> CurrentAsync(CancellationToken token) =>
            Task.FromResult(new CurrentPermissionsResponse(role, null, 0, 0, grants.Where(p => p.Value).Select(p => p.Key).ToList()));
    }
}

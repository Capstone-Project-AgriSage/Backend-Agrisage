using System.Net;
using System.Net.Http.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AgriSage.IntegrationTests.Api;

// F3.21 end to end: HTTP request -> JWT authentication (real account validator) -> role and permission filters (real permission
// evaluator on the database) -> service -> PostgreSQL. The API is hosted over the isolated schema of CreditTestDatabase.
public sealed class OwnerOnlyDebtEndToEndTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    [CreditDbFact]
    public async Task Delegated_sale_staff_is_refused_over_http_and_the_owner_is_let_through()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        var order = await e.FulfillAsync(10, token);
        var ownerId = e.User.UserId!.Value;
        Guid entryId, paymentId;
        using (var s = e.Scope())
        {
            entryId = (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, token)).Id;
            paymentId = (await s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>().RecordAsync(new(e.FarmerId, 3_000_000, Reference: "e2e"), token)).Id;
        }
        var staff = await OwnerOnlyDebtDatabaseTests.AddStaffAsync(e, OwnerOnlyDebtDatabaseTests.ByOverride);
        var before = await OwnerOnlyDebtDatabaseTests.SnapshotAsync(e, paymentId);

        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            // Real behaviour on purpose: no background workers, the real permission evaluator and account validator, and the
            // test's own schema instead of the (unconfigured) application database.
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IPermissionEvaluator>();
            services.AddScoped<IPermissionEvaluator>(sp => sp.GetRequiredService<PermissionEvaluator>());
            services.RemoveAll<IUserAccessValidator>();
            services.AddScoped<IUserAccessValidator, UserAccessValidator>();
            services.RemoveAll<DbContextOptions<AgriSageDbContext>>();
            services.RemoveAll<AgriSageDbContext>();
            services.AddDbContext<AgriSageDbContext>((p, o) => o.UseNpgsql(e.ConnectionString).AddInterceptors(
                p.GetRequiredService<SoftDeleteInterceptor>(), p.GetRequiredService<AuditableEntityInterceptor>(),
                p.GetRequiredService<ConcurrencyVersionInterceptor>()));
        }));
        // The real account validator needs a persisted, usable session whose security version matches the user's.
        async Task<HttpClient> AsAsync(Guid userId, string role)
        {
            var now = DateTimeOffset.UtcNow;
            Guid sessionId;
            long version;
            using (var s = e.Scope())
            {
                var context = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
                version = (await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId, token)).SecurityVersion;
                var session = new AuthSession(userId, version, now, now.AddHours(1));
                context.AuthSessions.Add(session);
                await context.SaveChangesAsync(token);
                sessionId = session.Id;
            }
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer",
                app.Services.GetRequiredService<IAccessTokenService>().Issue(userId, role, sessionId, version).Value);
            return client;
        }

        // Sale staff holding every owner-only permission by delegation: refused on each route, and nothing changed.
        using (var client = await AsAsync(staff.UserId, "SALES_STAFF"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/debt-entries/{entryId}/adjust", new { amount = 100, reason = "r" }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/debt-entries/{entryId}/cancel", new { reason = "r" }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/customers/{e.FarmerId}/debt/manual-entries", new { amount = 100, dueDate = "2026-12-01", reason = "r" }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/payments/{paymentId}/confirm", null, token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/payments/{paymentId}/reject", new { reason = "r" }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/customers/{e.FarmerId}/credit/suspend", new { reason = "r" }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/customers/{e.FarmerId}/credit/block", new { reason = "r" }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/customers/{e.FarmerId}/credit/activate", new { reason = "r" }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/debt-entries/{entryId}/change-due-date", new { newDueDate = "2099-12-01", reason = "r" }, token)).StatusCode);
        }
        Assert.Equal(before, await OwnerOnlyDebtDatabaseTests.SnapshotAsync(e, paymentId));

        // The owner of the store passes the same gates and the change is written.
        using (var client = await AsAsync(ownerId, "STORE_OWNER"))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/debt-entries/{entryId}/adjust", new { amount = 100, reason = "Price correction" }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/payments/{paymentId}/confirm", null, token)).StatusCode);
        }
        using var verify = e.Scope(); var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(PaymentStatus.Paid, (await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId, token)).Status);
        Assert.Equal(10_000_000 - 100 - 3_000_000, (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.Id == entryId, token)).OutstandingAmount);
    }
}

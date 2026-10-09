using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Audit;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.IntegrationTests.Infrastructure.Authentication;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Audit;

// Real PostgreSQL translation, identity resolution and store isolation; every fixture is rolled back.
public sealed class AuditHistoryDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [RealDbFact]
    public async Task Actor_filters_pagination_and_legacy_redaction_work_in_SQL_even_for_deleted_accounts()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        e.User.UpdateProfile("Audit Farmer", null);
        var first = new AuditLog("PAYMENT_RECEIVED", "PAYMENT", e.Time.UtcNow.AddMinutes(-1), e.Store.Id, e.User.Id,
            Guid.NewGuid(), newValues: "{\"amount\":100,\"nested\":{\"passwordHash\":\"sensitive-test-value\"}}");
        var failed = new AuditLog("PAYMENT_FAILED", "PAYMENT", e.Time.UtcNow, e.Store.Id, e.User.Id, Guid.NewGuid(),
            reason: "Payment rejected", userAgent: "Audit test browser");
        e.Context.AddRange(first, failed);
        await e.Context.SaveChangesAsync(Token);
        e.Current.Role = "ADMIN";
        var service = new AuditLogService(e.Context, e.Current);
        var request = new AuditLogListRequest { ActorUserId = e.User.Id, ActorRole = "farmer", Search = "audit farmer", PageSize = 1 };
        var page = await service.ListAsync(request, Token);
        Assert.Equal(2, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(failed.Id, item.Id); Assert.Equal("Audit Farmer", item.ActorName);
        Assert.Equal(e.User.Email, item.ActorEmail); Assert.Equal("FARMER", item.ActorRole); Assert.Equal("FAILURE", item.Status);
        Assert.Equal(first.Id, Assert.Single((await service.ListAsync(request with { Page = 2 }, Token)).Items).Id);
        Assert.Equal(failed.Id, Assert.Single((await service.ListAsync(request with { Status = "FAILURE" }, Token)).Items).Id);
        Assert.Equal(first.Id, Assert.Single((await service.ListAsync(request with { Status = "SUCCESS" }, Token)).Items).Id);
        Assert.Empty((await service.ListAsync(request with { ActorRole = "STORE_OWNER" }, Token)).Items);
        Assert.Equal(2, (await service.ListAsync(request with { Search = e.User.Email!.ToUpperInvariant() }, Token)).TotalCount);
        Assert.Equal(failed.Id, Assert.Single((await service.ListAsync(request with { Search = failed.EntityId!.Value.ToString() }, Token)).Items).Id);
        Assert.Equal(first.Id, Assert.Single((await service.ListAsync(request with { To = e.Time.UtcNow.AddSeconds(-1) }, Token)).Items).Id);
        var detail = await service.GetAsync(first.Id, Token);
        Assert.Equal("[REDACTED]", detail.NewValues!.Value.GetProperty("nested").GetProperty("passwordHash").GetString());
        Assert.Equal(100, detail.NewValues.Value.GetProperty("amount").GetInt32());
        e.Context.Users.Remove(e.User); await e.Context.SaveChangesAsync(Token);
        Assert.Equal("Audit Farmer", (await service.GetAsync(first.Id, Token)).ActorName);
        Assert.Equal(2, (await service.ListAsync(request, Token)).TotalCount);
        Assert.Contains("sensitive-test-value", (await e.Context.AuditLogs.AsNoTracking().SingleAsync(a => a.Id == first.Id, Token)).NewValues!);
    }

    [RealDbFact]
    public async Task Enrichment_and_search_never_expand_an_owners_store_scope_and_system_events_survive_left_joins()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var other = new Store("S" + Guid.NewGuid().ToString("N")[..12], "Other", "Address", "Province", status: StoreStatus.Inactive);
        var own = new AuditLog("INVENTORY_ADJUSTED", "STOCK_MOVEMENT", e.Time.UtcNow, e.Store.Id,
            e.User.Id, reason: "Audit scope test");
        var foreign = new AuditLog("INVENTORY_ADJUSTED", "STOCK_MOVEMENT", e.Time.UtcNow, other.Id,
            e.User.Id, reason: "Audit scope test");
        var system = new AuditLog("AUTH_MESSAGE_DELIVERY_FAILED", "USER", e.Time.UtcNow, reason: "Audit scope test");
        e.Context.AddRange(other, own, foreign, system, new StoreMember(e.Store.Id, e.User.Id));
        await e.Context.SaveChangesAsync(Token);
        e.Current.Role = "STORE_OWNER";
        var service = new AuditLogService(e.Context, e.Current);
        var request = new AuditLogListRequest { Search = "Audit scope test" };
        Assert.Equal(own.Id, Assert.Single((await service.ListAsync(request, Token)).Items).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(foreign.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(system.Id, Token));
        e.Current.Role = "ADMIN";
        Assert.Equal(3, (await service.ListAsync(request, Token)).TotalCount);
        Assert.Null((await service.GetAsync(system.Id, Token)).ActorName);
        Assert.Equal("FAILURE", (await service.GetAsync(system.Id, Token)).Status);
    }

    [RealDbFact]
    public async Task Login_records_the_verified_actor_and_failed_attempts_do_not_claim_identity_or_save_credentials()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        e.Current.UserId = null;
        var login = await e.Auth.LoginAsync(new LoginRequest(e.User.Email!, "initial-password"), Token);
        var success = await e.Context.AuditLogs.SingleAsync(a => a.EntityId == e.User.Id && a.Action == "AUTH_LOGIN", Token);
        Assert.Equal(e.User.Id, success.ActorUserId); Assert.Contains(login.SessionId!.Value.ToString(), success.NewValues!);
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => e.Auth.LoginAsync(new LoginRequest(e.User.Email!, "incorrect-password"), Token));
        var failure = await e.Context.AuditLogs.SingleAsync(a => a.EntityId == e.User.Id && a.Action == "AUTH_LOGIN_FAILED", Token);
        Assert.Null(failure.ActorUserId); Assert.Equal("INVALID_CREDENTIALS", failure.Reason);
        Assert.Null(failure.OldValues); Assert.Null(failure.NewValues);
        Assert.DoesNotContain("incorrect-password", failure.Reason);
    }
}

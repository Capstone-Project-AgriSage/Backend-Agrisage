using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Audit;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Notifications.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.BackgroundJobs;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Authentication;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Notifications;

public sealed class NotificationsOperationsDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [RealDbFact]
    public async Task Notifications_are_owned_paginated_and_archived_without_physical_deletion()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var other = new User(e.User.RoleId, "Other", "hash", $"{Guid.NewGuid():N}@example.test", null);
        var own = new Notification(e.User.Id, "SYSTEM", "Own", "Message", "{\"entityType\":\"ORDER\"}");
        var foreign = new Notification(other.Id, "SYSTEM", "Other", "Private");
        e.Context.AddRange(other, own, foreign); await e.Context.SaveChangesAsync(Token);
        var service = new NotificationService(e.Context, e.Current, e.Time, e.Locks);
        var page = await service.ListAsync(new NotificationListRequest { PageSize = 1 }, Token);
        Assert.Equal(1, page.TotalCount); Assert.Equal(own.Id, Assert.Single(page.Items).Id);
        Assert.Equal("ORDER", page.Items[0].Data!.Value.GetProperty("entityType").GetString());
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadAsync(foreign.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ArchiveAsync(foreign.Id, Token));
        Assert.Equal(1, (await service.UnreadCountAsync(Token)).Count);
        await service.ReadAsync(own.Id, Token); var readAt = own.ReadAt;
        e.Time.UtcNow = e.Time.UtcNow.AddMinutes(1); await service.ReadAsync(own.Id, Token);
        Assert.Equal(readAt, own.ReadAt); Assert.Equal(0, (await service.UnreadCountAsync(Token)).Count);
        await service.ArchiveAsync(own.Id, Token); await service.ReadAsync(own.Id, Token);
        Assert.Equal(NotificationStatus.Archived, own.Status);
        Assert.Empty((await service.ListAsync(new NotificationListRequest(), Token)).Items);
        Assert.Single((await service.ListAsync(new NotificationListRequest { Status = "ARCHIVED" }, Token)).Items);
        Assert.True(await e.Context.Notifications.AnyAsync(n => n.Id == own.Id, Token));
        Assert.Equal(NotificationStatus.Unread, foreign.Status);
    }
    [RealDbFact]
    public async Task Read_all_is_limited_to_the_current_user()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var other = new User(e.User.RoleId, "Other", "hash", $"{Guid.NewGuid():N}@example.test", null);
        var foreign = new Notification(other.Id, "SYSTEM", "Foreign", "Private");
        e.Context.AddRange(other, foreign, new Notification(e.User.Id, "SYSTEM", "Own1", "Message"),
            new Notification(e.User.Id, "SYSTEM", "Own2", "Message"));
        await e.Context.SaveChangesAsync(Token);
        var service = new NotificationService(e.Context, e.Current, e.Time, e.Locks);
        await service.ReadAllAsync(Token);
        Assert.Equal(0, (await service.UnreadCountAsync(Token)).Count); Assert.Equal(NotificationStatus.Unread, foreign.Status);
    }
    [RealDbFact]
    public async Task Outbox_and_business_audit_are_saved_atomically_and_dispatch_is_idempotent()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var order = new Order(e.Store.Id, $"OD-{Guid.NewGuid():N}", OrderSource.FarmerWeb, CustomerType.Registered,
            e.User.Id, "Farmer", SettlementType.FullPayment, FulfillmentType.Pickup, e.Farmer.Id);
        e.Context.Orders.Add(order);
        var log = e.Audit.Record("ORDER_CONFIRMED", "ORDER", order.Id, e.Store.Id);
        Assert.False(await e.Context.AuditLogs.AsNoTracking().AnyAsync(a => a.Id == log.Id, Token));
        Assert.False(await e.Context.NotificationOutbox.AsNoTracking().AnyAsync(o => o.AuditLogId == log.Id, Token));
        await e.Context.SaveChangesAsync(Token);
        var item = await e.Context.NotificationOutbox.SingleAsync(o => o.AuditLogId == log.Id, Token);
        var dispatcher = new NotificationDispatcher(e.Context, new NotificationOutboxLock(e.Context), new NotificationWriter(e.Context), e.Time);
        await dispatcher.DispatchAsync(item.Id, Token); await dispatcher.DispatchAsync(item.Id, Token);
        var notification = await e.Context.Notifications.SingleAsync(n => n.DeduplicationKey == $"audit:{log.Id:N}", Token);
        Assert.Equal(e.User.Id, notification.UserId); Assert.Equal("ORDER_STATUS_CHANGED", notification.NotificationType);
        Assert.NotNull(item.ProcessedAt);
    }
    [RealDbFact]
    public async Task Failed_save_does_not_commit_the_audit_or_its_outbox()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var log = e.Audit.Record("ORDER_CONFIRMED", "ORDER", Guid.NewGuid(), e.Store.Id);
        e.Context.AuthSessions.Add(new AuthSession(Guid.NewGuid(), 0, e.Time.UtcNow, e.Time.UtcNow.AddDays(1)));
        await Assert.ThrowsAsync<DbUpdateException>(() => e.Context.SaveChangesAsync(Token));
        await using var reader = e.Database.NewContext();
        Assert.False(await reader.AuditLogs.AnyAsync(a => a.Id == log.Id, Token));
        Assert.False(await reader.NotificationOutbox.AnyAsync(o => o.AuditLogId == log.Id, Token));
    }
    [RealDbFact]
    public async Task Notification_deduplication_survives_archive_and_soft_delete()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var writer = new NotificationWriter(e.Context);
        await writer.AddAsync([e.User.Id], "SYSTEM", "Title", "Message", "same-event", "ORDER", Guid.NewGuid(), Token);
        await e.Context.SaveChangesAsync(Token);
        var n = await e.Context.Notifications.SingleAsync(n => n.DeduplicationKey == "same-event", Token);
        n.Archive(); e.Context.Remove(n); await e.Context.SaveChangesAsync(Token);
        await writer.AddAsync([e.User.Id], "SYSTEM", "Title", "Message", "same-event", "ORDER", Guid.NewGuid(), Token);
        await e.Context.SaveChangesAsync(Token);
        Assert.Equal(1, await e.Context.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == e.User.Id && n.DeduplicationKey == "same-event", Token));
    }
    [RealDbFact]
    public async Task Retry_backoff_is_persisted_and_prevents_immediate_redispatch()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var log = e.Audit.Record("ORDER_CONFIRMED", "ORDER", Guid.NewGuid(), e.Store.Id);
        await e.Context.SaveChangesAsync(Token);
        var id = await e.Context.NotificationOutbox.Where(o => o.AuditLogId == log.Id).Select(o => o.Id).SingleAsync(Token);
        var dispatcher = new NotificationDispatcher(e.Context, new NotificationOutboxLock(e.Context), new NotificationWriter(e.Context), e.Time);
        await dispatcher.RetryAsync(id, Token);
        Assert.DoesNotContain(id, await dispatcher.DueAsync(100, Token));
        e.Time.UtcNow = e.Time.UtcNow.AddSeconds(61);
        Assert.Contains(id, await dispatcher.DueAsync(100, Token));
    }
    [RealDbFact]
    public async Task Debt_reminders_deduplicate_daily_without_mutating_the_ledger()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var account = new DebtAccount(e.Store.Id, e.Farmer.Id);
        var posting = account.CreateManualAdjustmentEntry($"DE-{Guid.NewGuid():N}", 100_000m,
            BusinessCalendar.Today(e.Time.UtcNow).AddDays(-1), e.User.Id, e.Time.UtcNow);
        e.Context.DebtAccounts.Add(account); e.Context.DebtEntries.Add(posting.Entry); e.Context.DebtTransactions.Add(posting.Transaction);
        await e.Context.SaveChangesAsync(Token);
        var ledgerCount = await e.Context.DebtTransactions.CountAsync(t => t.DebtAccountId == account.Id, Token);
        var service = new OperationalAlertsService(e.Context, new NotificationWriter(e.Context),
            new InventoryService(e.Context, e.Time, new RowLockService(e.Context)), e.Time);
        await service.DebtRemindersAsync(10, 3, Token); await service.DebtRemindersAsync(10, 3, Token);
        Assert.Equal(1, await e.Context.Notifications.CountAsync(n => n.UserId == e.User.Id && n.NotificationType == "DEBT_OVERDUE", Token));
        Assert.Equal(100_000m, account.CurrentBalance);
        Assert.Equal(ledgerCount, await e.Context.DebtTransactions.CountAsync(t => t.DebtAccountId == account.Id, Token));
        e.Time.UtcNow = e.Time.UtcNow.AddDays(1); await service.DebtRemindersAsync(10, 3, Token);
        Assert.Equal(2, await e.Context.Notifications.CountAsync(n => n.UserId == e.User.Id && n.NotificationType == "DEBT_OVERDUE", Token));
    }
    [RealDbFact]
    public async Task Audit_history_is_admin_or_active_owner_only_and_owner_cannot_see_global_or_other_store_events()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var otherStore = new Store("S" + Guid.NewGuid().ToString("N")[..12], "Other", "Address", "Province", status: StoreStatus.Inactive);
        var own = new AuditLog("OWN_EVENT", "ORDER", e.Time.UtcNow, e.Store.Id, e.User.Id);
        var foreign = new AuditLog("FOREIGN_EVENT", "ORDER", e.Time.UtcNow, otherStore.Id, e.User.Id);
        var global = new AuditLog("PASSWORD_RESET", "USER", e.Time.UtcNow, actorUserId: e.User.Id);
        e.Context.AddRange(otherStore, own, foreign, global); await e.Context.SaveChangesAsync(Token);
        var service = new AuditLogService(e.Context, e.Current);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(new AuditLogListRequest(), Token));
        e.Current.Role = "STORE_OWNER";
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(new AuditLogListRequest(), Token));
        e.Context.StoreMembers.Add(new StoreMember(e.Store.Id, e.User.Id)); await e.Context.SaveChangesAsync(Token);
        Assert.Equal(own.Id, Assert.Single((await service.ListAsync(new AuditLogListRequest { Action = "own_event" }, Token)).Items).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(foreign.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(global.Id, Token));
        e.Current.Role = "ADMIN";
        Assert.Equal(global.Id, (await service.GetAsync(global.Id, Token)).Id);
        Assert.Equal(foreign.Id, (await service.GetAsync(foreign.Id, Token)).Id);
    }
    [RealDbFact]
    public async Task Authentication_maintenance_expires_records_without_physical_deletion()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var session = await e.StartAsync();
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token);
        e.Time.UtcNow = e.Time.UtcNow.AddDays(31);
        await new AuthMaintenanceService(e.Context, e.Time).ExpireAsync(100, Token);
        Assert.NotNull((await e.Context.AuthSessions.SingleAsync(s => s.Id == session.SessionId, Token)).RevokedAt);
        Assert.NotNull((await e.Context.AuthChallenges.SingleAsync(c => c.UserId == e.User.Id, Token)).ConsumedAt);
        Assert.True(await e.Context.RefreshTokens.AnyAsync(t => t.SessionId == session.SessionId, Token));
    }
    [RealDbFact]
    public async Task PostgreSql_job_lock_excludes_other_replica_and_releases_on_dispose()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var locks = new PostgresBackgroundJobLock(e.Context);
        var first = await locks.TryAcquireAsync(AgriSage.Application.Common.Interfaces.BackgroundTask.Notifications, Token);
        Assert.NotNull(first);
        try { Assert.Null(await locks.TryAcquireAsync(AgriSage.Application.Common.Interfaces.BackgroundTask.Notifications, Token)); }
        finally { await first.DisposeAsync(); }
        await using var next = await locks.TryAcquireAsync(AgriSage.Application.Common.Interfaces.BackgroundTask.Notifications, Token);
        Assert.NotNull(next);
    }
}

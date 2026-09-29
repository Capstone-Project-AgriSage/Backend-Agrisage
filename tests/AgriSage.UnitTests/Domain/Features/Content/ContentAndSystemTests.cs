using System.Reflection;
using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Content.Entities;
using AgriSage.Domain.Features.Content.Enums;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Notifications.Enums;

namespace AgriSage.UnitTests.Domain.Features.Content;

public class ContentAndSystemTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Published_article_always_has_a_publish_time()
    {
        var article = new Article("Rice blast", "rice-blast", "Content", Guid.NewGuid());
        Assert.Null(article.PublishedAt);

        article.Publish(Now);

        Assert.Equal(ArticleStatus.Published, article.Status);
        Assert.Equal(Now, article.PublishedAt);
    }

    [Fact]
    public void Resolved_contact_request_always_has_a_resolution_time()
    {
        var request = new ContactRequest("CR-0001", "Nguyen Van A", "Delivery late", "My order is late");

        Assert.Throws<DomainException>(() => request.ChangeStatus(ContactRequestStatus.Resolved));

        request.Resolve(Now, "Delivered next morning");

        Assert.Equal(ContactRequestStatus.Resolved, request.Status);
        Assert.Equal(Now, request.ResolvedAt);
    }

    [Fact]
    public void Read_notification_always_has_a_read_time()
    {
        var notification = new Notification(Guid.NewGuid(), "ORDER_STATUS_CHANGED", "Order confirmed", "SO-0001 confirmed");
        Assert.Equal(NotificationStatus.Unread, notification.Status);

        notification.MarkRead(Now);

        Assert.Equal(NotificationStatus.Read, notification.Status);
        Assert.Equal(Now, notification.ReadAt);
    }

    [Fact]
    public void Audit_log_is_append_only()
    {
        var auditLog = new AuditLog("PRICE_OVERRIDE", "ORDER_ITEM", Now, entityId: Guid.NewGuid(), reason: "Loyal customer");

        var publicMethods = typeof(AuditLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

        Assert.Empty(publicMethods);
        Assert.False(typeof(AuditLog).IsAssignableTo(typeof(SoftDeletableEntity)));
        Assert.Equal(Now, auditLog.OccurredAt);
        Assert.Throws<DomainException>(() => new AuditLog(" ", "ORDER_ITEM", Now));
    }
}

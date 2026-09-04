using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.SharedKernel;

namespace HR.Modules.Notifications.Tests;

/// <summary>
/// OBT-REM-12: <see cref="NotificationCreatedAuditEvent"/>'s EventId must be deterministic
/// (== NotificationId) rather than a random Guid — this is what makes republishing it from
/// ReconcileMissingNotificationAuditsJob or NotificationWriter.RepairExistingNotificationAsync a
/// guaranteed no-op instead of ever creating a duplicate audit row.
/// </summary>
public class NotificationsAuditTests
{
    [Fact]
    public void NotificationCreatedAuditEvent_EventId_Equals_NotificationId()
    {
        var notificationId = Guid.NewGuid();
        var evt = new NotificationCreatedAuditEvent(
            Guid.NewGuid(), notificationId, Guid.NewGuid(),
            NotificationType.LeaveApproved, NotificationChannel.Both, DateTimeOffset.UtcNow);

        Assert.Equal(notificationId, ((IAuditEvent)evt).EventId);
        Assert.Equal(notificationId, ((IAuditEvent)evt).EntityId);
    }

    [Fact]
    public void NotificationCreatedAuditEvent_EventId_Is_Stable_Across_Multiple_Instances_With_Same_NotificationId()
    {
        // Republishing (e.g. the repair/reconciliation paths) constructs a brand-new record instance
        // each time — EventId must still resolve to the exact same value both times so the audit
        // store's unique-EventId dedupe actually catches the duplicate.
        var notificationId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var first = new NotificationCreatedAuditEvent(
            companyId, notificationId, employeeId, NotificationType.LeaveApproved, NotificationChannel.Both, occurredAt);
        var second = new NotificationCreatedAuditEvent(
            companyId, notificationId, employeeId, NotificationType.LeaveApproved, NotificationChannel.Both, occurredAt);

        Assert.Equal(((IAuditEvent)first).EventId, ((IAuditEvent)second).EventId);
    }

    [Fact]
    public void NotificationCreatedAuditEvent_EventId_Differs_For_Different_Notifications()
    {
        var evtA = new NotificationCreatedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            NotificationType.LeaveApproved, NotificationChannel.Both, DateTimeOffset.UtcNow);
        var evtB = new NotificationCreatedAuditEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            NotificationType.LeaveApproved, NotificationChannel.Both, DateTimeOffset.UtcNow);

        Assert.NotEqual(((IAuditEvent)evtA).EventId, ((IAuditEvent)evtB).EventId);
    }
}

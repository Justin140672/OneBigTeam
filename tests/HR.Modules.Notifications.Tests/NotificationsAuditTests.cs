using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.SharedKernel;

namespace HR.Modules.Notifications.Tests;

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

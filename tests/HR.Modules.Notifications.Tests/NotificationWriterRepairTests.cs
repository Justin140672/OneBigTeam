using System.Reflection;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

/// <summary>
/// OBT-REM-12: <see cref="NotificationWriter"/>'s private RepairExistingNotificationAsync — invoked
/// when TrySaveIdempotentlyAsync signals a duplicate (a real PostgreSQL 23505 unique-constraint
/// violation on the (employee_id, source_entity_id, type) index) — can only be triggered end-to-end
/// via a real Postgres-backed race (see HR.Integration.Tests' NotificationWriterConcurrencyTests for
/// that full trigger path: EF Core's InMemory provider does not enforce unique indexes or surface
/// Npgsql's PostgresException shape, so TrySaveIdempotentlyAsync's catch clause is unreachable
/// against it). These tests instead invoke the already-triggered repair method directly via
/// reflection to pin down its own effects in isolation, against a NotificationsDbContext seeded to
/// look exactly like the "existing row from an earlier, crashed attempt" scenario it exists to repair.
/// </summary>
public class NotificationWriterRepairTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Task RepairAsync(
        NotificationWriter writer, Guid employeeId, Guid sourceEntityId, NotificationType type) =>
        (Task)typeof(NotificationWriter)
            .GetMethod("RepairExistingNotificationAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(writer, [employeeId, sourceEntityId, type, CancellationToken.None])!;

    [Fact]
    public async Task Repair_Republishes_Audit_And_Enqueues_Job_When_Existing_Delivery_Is_Pending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        var notification = Notification.Create(
            notificationId, companyId, employeeId, "Leave approved", "body",
            sourceEntityId, Now, NotificationType.LeaveApproved);
        db.Notifications.Add(notification);
        var delivery = EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, Now);
        db.EmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var writer = new NotificationWriter(db, backgroundJobClient, auditPublisher, new FakeCompanyNotificationSettingsReader());

        await RepairAsync(writer, employeeId, sourceEntityId, NotificationType.LeaveApproved);

        var evt = Assert.Single(auditPublisher.Published);
        var created = Assert.IsType<NotificationCreatedAuditEvent>(evt);
        Assert.Equal(companyId, created.CompanyId);
        Assert.Equal(notificationId, created.NotificationId);
        Assert.Equal(employeeId, created.RecipientEmployeeId);
        Assert.Equal(NotificationType.LeaveApproved, created.NotificationType);

        var job = Assert.Single(backgroundJobClient.CreatedJobs);
        Assert.Equal(typeof(EmailDeliveryJob), job.Type);
        Assert.Equal(nameof(EmailDeliveryJob.SendAsync), job.Method.Name);
        Assert.Equal(notificationId, job.Args[0]);
        Assert.Equal(companyId, job.Args[1]);

        // No new rows created — exactly the pre-existing ones remain.
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Single(await db.EmailDeliveries.ToListAsync());
    }

    // Theory parameters must be a publicly accessible type (xUnit requires public test methods),
    // but EmailDeliveryStatus is internal — pass the enum's underlying int value instead and cast.
    [Theory]
    [InlineData((int)EmailDeliveryStatus.Sent)]
    [InlineData((int)EmailDeliveryStatus.Skipped)]
    [InlineData((int)EmailDeliveryStatus.Failed)]
    public async Task Repair_Does_Not_Enqueue_Job_When_Existing_Delivery_Is_Not_Pending(int statusValue)
    {
        var status = (EmailDeliveryStatus)statusValue;
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        var notification = Notification.Create(
            notificationId, companyId, employeeId, "Leave approved", "body",
            sourceEntityId, Now, NotificationType.LeaveApproved);
        db.Notifications.Add(notification);
        var delivery = EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, Now);
        switch (status)
        {
            case EmailDeliveryStatus.Sent:
                delivery.RecordAttempt(Now);
                delivery.MarkSent(Now);
                break;
            case EmailDeliveryStatus.Skipped:
                delivery.MarkSkipped("Email notifications disabled for this company.");
                break;
            case EmailDeliveryStatus.Failed:
                delivery.RecordAttempt(Now);
                delivery.MarkFailed("Invalid recipient address.");
                break;
        }
        db.EmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var writer = new NotificationWriter(db, backgroundJobClient, auditPublisher, new FakeCompanyNotificationSettingsReader());

        await RepairAsync(writer, employeeId, sourceEntityId, NotificationType.LeaveApproved);

        // Audit is still (unconditionally) republished — only the enqueue is gated on Pending.
        Assert.Single(auditPublisher.Published);
        Assert.Empty(backgroundJobClient.CreatedJobs);
    }

    [Fact]
    public async Task Repair_Does_Not_Enqueue_Job_When_No_EmailDelivery_Row_Exists()
    {
        // In-app-only notification type — the existing notification was never channel-eligible for
        // email, so there is no EmailDelivery row to repair.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        var notification = Notification.Create(
            notificationId, companyId, employeeId, "Task assigned", "body",
            sourceEntityId, Now, NotificationType.TaskAssigned);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var writer = new NotificationWriter(db, backgroundJobClient, auditPublisher, new FakeCompanyNotificationSettingsReader());

        await RepairAsync(writer, employeeId, sourceEntityId, NotificationType.TaskAssigned);

        var evt = Assert.Single(auditPublisher.Published);
        Assert.IsType<NotificationCreatedAuditEvent>(evt);
        Assert.Empty(backgroundJobClient.CreatedJobs);
    }

    [Fact]
    public async Task Repair_Is_A_NoOp_When_No_Matching_Notification_Exists()
    {
        // Defensive branch: should not happen (a unique-violation implies a row exists), but must not
        // throw and must not publish/enqueue anything if it somehow does.
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var writer = new NotificationWriter(db, backgroundJobClient, auditPublisher, new FakeCompanyNotificationSettingsReader());

        await RepairAsync(writer, Guid.NewGuid(), Guid.NewGuid(), NotificationType.LeaveApproved);

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(backgroundJobClient.CreatedJobs);
    }
}

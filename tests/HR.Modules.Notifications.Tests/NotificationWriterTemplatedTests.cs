using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

// NOT-03: NotificationWriter.WriteTemplatedAsync — template-based write path. See
// NotificationWriterTests for the pre-existing WriteAsync coverage this deliberately does not
// duplicate.
public class NotificationWriterTemplatedTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteTemplatedAsync_LeaveApproved_Persists_Notification_And_EmailDelivery_And_Enqueues_Job()
    {
        await using var ctx = BuildContext();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var writer = new NotificationWriter(ctx, backgroundJobClient, new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var tokens = new Dictionary<string, string>
        {
            ["StartDate"] = "3 Aug 2026",
            ["EndDate"] = "7 Aug 2026",
        };

        var result = await writer.WriteTemplatedAsync(
            id, companyId, employeeId, NotificationType.LeaveApproved, tokens,
            sourceEntityId, NotificationPriority.Normal, Now);

        Assert.True(result.IsSuccess);

        var notification = await ctx.Notifications.SingleAsync();
        Assert.Equal("Your leave request has been approved", notification.Title);
        Assert.Equal("Your leave from 3 Aug 2026 to 7 Aug 2026 has been approved.", notification.Body);

        var delivery = await ctx.EmailDeliveries.SingleAsync();
        Assert.Equal(id, delivery.NotificationId);
        Assert.Equal(1, delivery.TemplateVersion);
        Assert.Equal("Your leave request has been approved", delivery.EmailSubject);
        Assert.Contains("Your leave from 3 Aug 2026 to 7 Aug 2026 has been approved.", delivery.EmailBody);

        var job = Assert.Single(backgroundJobClient.CreatedJobs);
        Assert.Equal(typeof(EmailDeliveryJob), job.Type);
        Assert.Equal(nameof(EmailDeliveryJob.SendAsync), job.Method.Name);
        Assert.Equal(id, job.Args[0]);
    }

    // NOT-05: audit -------------------------------------------------------------------------------

    [Fact]
    public async Task WriteTemplatedAsync_Publishes_NotificationCreatedAuditEvent_On_Success()
    {
        await using var ctx = BuildContext();
        var auditPublisher = new FakeAuditPublisher();
        var writer = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), auditPublisher, new FakeCompanyNotificationSettingsReader());
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var tokens = new Dictionary<string, string>
        {
            ["StartDate"] = "3 Aug 2026",
            ["EndDate"] = "7 Aug 2026",
        };

        var result = await writer.WriteTemplatedAsync(
            id, companyId, employeeId, NotificationType.LeaveApproved, tokens,
            sourceEntityId, NotificationPriority.Normal, Now);

        Assert.True(result.IsSuccess);

        var evt = Assert.Single(auditPublisher.Published);
        var created = Assert.IsType<NotificationCreatedAuditEvent>(evt);
        Assert.Equal(companyId,                     created.CompanyId);
        Assert.Equal(id,                             created.NotificationId);
        Assert.Equal(employeeId,                     created.RecipientEmployeeId);
        Assert.Equal(NotificationType.LeaveApproved, created.NotificationType);
        Assert.Equal(NotificationsSystemActor.Id,    ((HR.SharedKernel.IAuditEvent)created).ActorEmployeeId);
    }

    [Fact]
    public async Task WriteTemplatedAsync_TaskAssigned_Persists_Notification_But_Not_EmailDelivery_Or_Job()
    {
        await using var ctx = BuildContext();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var writer = new NotificationWriter(ctx, backgroundJobClient, new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var tokens = new Dictionary<string, string>
        {
            ["TaskTitle"] = "Review leave request",
            ["TaskDescription"] = "Some detail",
        };

        var result = await writer.WriteTemplatedAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskAssigned, tokens,
            Guid.NewGuid(), NotificationPriority.Normal, Now);

        Assert.True(result.IsSuccess);

        var notification = await ctx.Notifications.SingleAsync();
        Assert.Equal("New task assigned: Review leave request", notification.Title);
        Assert.Equal("Some detail", notification.Body);

        Assert.Empty(ctx.EmailDeliveries);
        Assert.Empty(backgroundJobClient.CreatedJobs);
    }

    [Fact]
    public async Task WriteTemplatedAsync_Missing_Required_Token_Fails_Without_Persisting_Or_Enqueuing()
    {
        await using var ctx = BuildContext();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var auditPublisher = new FakeAuditPublisher();
        var writer = new NotificationWriter(ctx, backgroundJobClient, auditPublisher, new FakeCompanyNotificationSettingsReader());
        var tokens = new Dictionary<string, string> { ["StartDate"] = "3 Aug 2026" }; // EndDate missing

        var result = await writer.WriteTemplatedAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationType.LeaveApproved, tokens,
            Guid.NewGuid(), NotificationPriority.Normal, Now);

        Assert.True(result.IsFailure);
        Assert.Contains("EndDate", result.Error.Message);
        Assert.Empty(ctx.Notifications);
        Assert.Empty(ctx.EmailDeliveries);
        Assert.Empty(backgroundJobClient.CreatedJobs);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task WriteTemplatedAsync_With_Unregistered_NotificationType_Throws_InvalidOperationException()
    {
        await using var ctx = BuildContext();
        var writer = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteTemplatedAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskOverdue,
            new Dictionary<string, string>(), Guid.NewGuid(), NotificationPriority.Normal, Now));
    }

    // NOT-04: ActionUrl computed at write time -------------------------------------------------

    [Fact]
    public async Task WriteTemplatedAsync_LeaveApproved_Persists_ActionUrl_Matching_NotificationActionRouteBuilder()
    {
        await using var ctx = BuildContext();
        var writer = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var tokens = new Dictionary<string, string>
        {
            ["StartDate"] = "3 Aug 2026",
            ["EndDate"] = "7 Aug 2026",
        };

        var result = await writer.WriteTemplatedAsync(
            Guid.NewGuid(), companyId, employeeId, NotificationType.LeaveApproved, tokens,
            sourceEntityId, NotificationPriority.Normal, Now);

        Assert.True(result.IsSuccess);

        var expected = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.LeaveApproved, companyId, employeeId, sourceEntityId);

        var notification = await ctx.Notifications.SingleAsync();
        Assert.NotNull(expected);
        Assert.Equal(expected, notification.ActionUrl);
    }

    [Fact]
    public async Task WriteTemplatedAsync_TaskAssigned_Persists_ActionUrl_Matching_NotificationActionRouteBuilder()
    {
        await using var ctx = BuildContext();
        var writer = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher(), new FakeCompanyNotificationSettingsReader());
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var tokens = new Dictionary<string, string>
        {
            ["TaskTitle"] = "Review leave request",
            ["TaskDescription"] = "Some detail",
        };

        var result = await writer.WriteTemplatedAsync(
            Guid.NewGuid(), companyId, employeeId, NotificationType.TaskAssigned, tokens,
            sourceEntityId, NotificationPriority.Normal, Now);

        Assert.True(result.IsSuccess);

        var expected = NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.TaskAssigned, companyId, employeeId, sourceEntityId);

        var notification = await ctx.Notifications.SingleAsync();
        Assert.NotNull(expected);
        Assert.Equal(expected, notification.ActionUrl);
    }

    // SET-06: notification-channel settings ----------------------------------------------------

    [Fact]
    public async Task WriteTemplatedAsync_Scheduled_Reminder_Type_With_ScheduledRemindersEnabled_False_Is_A_NoOp()
    {
        await using var ctx = BuildContext();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var auditPublisher = new FakeAuditPublisher();
        var settingsReader = new FakeCompanyNotificationSettingsReader(
            new CompanyNotificationSettings(true, false));
        var writer = new NotificationWriter(ctx, backgroundJobClient, auditPublisher, settingsReader);
        var tokens = new Dictionary<string, string>
        {
            ["DocumentTitle"] = "Passport",
            ["DocumentTypeName"] = "ID Document",
            ["DaysUntilExpiry"] = "5",
            ["ExpiryDate"] = "20 Aug 2026",
        };

        var result = await writer.WriteTemplatedAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationType.DocumentExpiring, tokens,
            Guid.NewGuid(), NotificationPriority.Normal, Now);

        Assert.True(result.IsSuccess);
        Assert.Empty(ctx.Notifications);
        Assert.Empty(ctx.EmailDeliveries);
        Assert.Empty(backgroundJobClient.CreatedJobs);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task WriteTemplatedAsync_Scheduled_Reminder_Type_With_ScheduledRemindersEnabled_True_Behaves_As_Before()
    {
        await using var ctx = BuildContext();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var settingsReader = new FakeCompanyNotificationSettingsReader(
            new CompanyNotificationSettings(true, true));
        var writer = new NotificationWriter(ctx, backgroundJobClient, new FakeAuditPublisher(), settingsReader);
        var tokens = new Dictionary<string, string>
        {
            ["DocumentTitle"] = "Passport",
            ["DocumentTypeName"] = "ID Document",
            ["DaysUntilExpiry"] = "5",
            ["ExpiryDate"] = "20 Aug 2026",
        };

        var result = await writer.WriteTemplatedAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NotificationType.DocumentExpiring, tokens,
            Guid.NewGuid(), NotificationPriority.Normal, Now);

        Assert.True(result.IsSuccess);
        Assert.Single(await ctx.Notifications.ToListAsync());
        // DocumentExpiring is not an email-eligible type — no EmailDelivery regardless of setting.
        Assert.Empty(ctx.EmailDeliveries);
        Assert.Empty(backgroundJobClient.CreatedJobs);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}

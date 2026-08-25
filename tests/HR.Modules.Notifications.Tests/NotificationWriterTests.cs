using HR.Modules.Notifications;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class NotificationWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsync_Persists_Notification()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();
        var id               = Guid.NewGuid();

        await writer.WriteAsync(
            id, companyId, employeeId,
            "Task assigned", null,
            sourceEntityId, NotificationType.TaskAssigned, NotificationPriority.Normal,
            Now);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.Equal(id,              saved.Id);
        Assert.Equal(companyId,       saved.CompanyId);
        Assert.Equal(employeeId,      saved.EmployeeId);
        Assert.Equal("Task assigned", saved.Title);
        Assert.False(saved.IsRead);
    }

    // NOT-05: audit -------------------------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_Publishes_NotificationCreatedAuditEvent()
    {
        await using var ctx  = BuildContext();
        var auditPublisher   = new FakeAuditPublisher();
        var writer           = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), auditPublisher);
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();
        var id               = Guid.NewGuid();

        await writer.WriteAsync(
            id, companyId, employeeId,
            "Leave approved", null,
            sourceEntityId, NotificationType.LeaveApproved, NotificationPriority.Normal,
            Now);

        var evt = Assert.Single(auditPublisher.Published);
        var created = Assert.IsType<NotificationCreatedAuditEvent>(evt);
        Assert.Equal(companyId,                       created.CompanyId);
        Assert.Equal(id,                               created.NotificationId);
        Assert.Equal(employeeId,                       created.RecipientEmployeeId);
        Assert.Equal(NotificationType.LeaveApproved,   created.NotificationType);
        Assert.Equal(NotificationChannel.Both,         created.Channel);
        Assert.Equal(NotificationsSystemActor.Id,      ((HR.SharedKernel.IAuditEvent)created).ActorEmployeeId);
    }

    [Fact]
    public async Task WriteAsync_Publishes_NotificationCreatedAuditEvent_With_InApp_Channel_For_InApp_Only_Type()
    {
        await using var ctx  = BuildContext();
        var auditPublisher   = new FakeAuditPublisher();
        var writer           = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), auditPublisher);

        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Task assigned", null,
            Guid.NewGuid(), NotificationType.TaskAssigned, NotificationPriority.Normal,
            Now);

        var evt = Assert.Single(auditPublisher.Published);
        var created = Assert.IsType<NotificationCreatedAuditEvent>(evt);
        Assert.Equal(NotificationChannel.InApp, created.Channel);
    }

    [Fact]
    public async Task ExistsAsync_Returns_True_When_Notification_Exists()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());
        var employeeId       = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();

        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), employeeId,
            "T", null, sourceEntityId,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now);

        var exists = await writer.ExistsAsync(employeeId, sourceEntityId, NotificationType.TaskDueSoon);
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_When_Notification_Does_Not_Exist()
    {
        await using var ctx = BuildContext();
        var writer          = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());

        var exists = await writer.ExistsAsync(Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskDueSoon);
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_When_Type_Does_Not_Match()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());
        var employeeId       = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();

        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), employeeId,
            "T", null, sourceEntityId,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now);

        var exists = await writer.ExistsAsync(employeeId, sourceEntityId, NotificationType.TaskAssigned);
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_When_SourceEntityId_Does_Not_Match()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());
        var employeeId       = Guid.NewGuid();

        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), employeeId,
            "T", null, Guid.NewGuid(),
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now);

        var exists = await writer.ExistsAsync(employeeId, Guid.NewGuid(), NotificationType.TaskDueSoon);
        Assert.False(exists);
    }

    [Fact]
    public async Task GetLastSentAtAsync_Returns_Null_When_None_Exists()
    {
        await using var ctx = BuildContext();
        var writer          = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());

        var result = await writer.GetLastSentAtAsync(Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskDueSoon);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLastSentAtAsync_Returns_Most_Recent_CreatedAt_When_Multiple_Exist()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());
        var employeeId       = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();

        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), employeeId,
            "Older", null, sourceEntityId,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now.AddHours(-2));
        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), employeeId,
            "Newest", null, sourceEntityId,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now);
        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), employeeId,
            "Middle", null, sourceEntityId,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now.AddHours(-1));

        var result = await writer.GetLastSentAtAsync(employeeId, sourceEntityId, NotificationType.TaskDueSoon);
        Assert.Equal(Now, result);
    }

    [Fact]
    public async Task GetLastSentAtAsync_Ignores_NonMatching_Type()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());
        var employeeId       = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();

        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), employeeId,
            "T", null, sourceEntityId,
            NotificationType.TaskAssigned, NotificationPriority.Normal, Now);

        var result = await writer.GetLastSentAtAsync(employeeId, sourceEntityId, NotificationType.TaskDueSoon);
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveBySourceEntityAsync_Returns_Zero_When_None_Match()
    {
        await using var ctx = BuildContext();
        var writer          = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());

        var removed = await writer.RemoveBySourceEntityAsync(Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskDueSoon);
        Assert.Equal(0, removed);
    }

    [Fact]
    public async Task RemoveBySourceEntityAsync_Removes_Only_Matching_Notifications()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new HR.Modules.Notifications.Tests.Infrastructure.NoOpBackgroundJobClient(), new HR.Modules.Notifications.Tests.Infrastructure.FakeAuditPublisher());
        var companyId        = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();

        await writer.WriteAsync(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Match 1", null, sourceEntityId,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now);
        await writer.WriteAsync(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Match 2", null, sourceEntityId,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now);
        // Different type - should not be removed
        await writer.WriteAsync(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Different type", null, sourceEntityId,
            NotificationType.TaskAssigned, NotificationPriority.Normal, Now);
        // Different company - should not be removed
        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Different company", null, sourceEntityId,
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now);
        // Different source entity - should not be removed
        await writer.WriteAsync(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Different source", null, Guid.NewGuid(),
            NotificationType.TaskDueSoon, NotificationPriority.Normal, Now);

        var removed = await writer.RemoveBySourceEntityAsync(companyId, sourceEntityId, NotificationType.TaskDueSoon);

        Assert.Equal(2, removed);
        var remainingTitles = await ctx.Notifications.Select(n => n.Title).OrderBy(t => t).ToListAsync();
        Assert.Equal(
            new[] { "Different company", "Different source", "Different type" },
            remainingTitles);
    }

    // NOT-02: channel-aware delivery ------------------------------------------------------------

    [Fact]
    public async Task WriteAsync_Persists_EmailDelivery_And_Enqueues_Job_For_Email_Eligible_Type()
    {
        await using var ctx    = BuildContext();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var writer              = new NotificationWriter(ctx, backgroundJobClient, new FakeAuditPublisher());
        var companyId           = Guid.NewGuid();
        var employeeId          = Guid.NewGuid();
        var sourceEntityId      = Guid.NewGuid();
        var id                  = Guid.NewGuid();

        await writer.WriteAsync(
            id, companyId, employeeId,
            "Leave approved", null,
            sourceEntityId, NotificationType.LeaveApproved, NotificationPriority.Normal,
            Now);

        var delivery = await ctx.EmailDeliveries.SingleAsync();
        Assert.Equal(id,        delivery.NotificationId);
        Assert.Equal(id,        delivery.IdempotencyKey);
        Assert.Equal(companyId, delivery.CompanyId);
        Assert.Equal(HR.Modules.Notifications.Domain.EmailDeliveryStatus.Pending, delivery.Status);

        var job = Assert.Single(backgroundJobClient.CreatedJobs);
        Assert.Equal(typeof(EmailDeliveryJob), job.Type);
        Assert.Equal(nameof(EmailDeliveryJob.SendAsync), job.Method.Name);
        Assert.Equal(id, job.Args[0]);
    }

    [Fact]
    public async Task WriteAsync_Does_Not_Persist_EmailDelivery_Or_Enqueue_Job_For_InApp_Only_Type()
    {
        await using var ctx    = BuildContext();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var writer              = new NotificationWriter(ctx, backgroundJobClient, new FakeAuditPublisher());

        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Task assigned", null,
            Guid.NewGuid(), NotificationType.TaskAssigned, NotificationPriority.Normal,
            Now);

        Assert.Empty(ctx.EmailDeliveries);
        Assert.Empty(backgroundJobClient.CreatedJobs);
    }

    // NOT-04: ActionUrl computed at write time -------------------------------------------------

    [Fact]
    public async Task WriteAsync_Persists_ActionUrl_Matching_NotificationActionRouteBuilder_For_Mapped_Type()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher());
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();
        var id               = Guid.NewGuid();

        await writer.WriteAsync(
            id, companyId, employeeId,
            "Task assigned", null,
            sourceEntityId, NotificationType.TaskAssigned, NotificationPriority.Normal,
            Now);

        var expected = HR.Modules.Notifications.Domain.NotificationActionRouteBuilder.BuildActionUrl(
            NotificationType.TaskAssigned, companyId, employeeId, sourceEntityId);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.NotNull(expected);
        Assert.Equal(expected, saved.ActionUrl);
    }

    [Fact]
    public async Task WriteAsync_Persists_Null_ActionUrl_For_Type_With_No_Destination()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new NoOpBackgroundJobClient(), new FakeAuditPublisher());

        await writer.WriteAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Interview scheduled", null,
            Guid.NewGuid(), NotificationType.InterviewScheduled, NotificationPriority.Normal,
            Now);

        var saved = await ctx.Notifications.SingleAsync();
        Assert.Null(saved.ActionUrl);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}

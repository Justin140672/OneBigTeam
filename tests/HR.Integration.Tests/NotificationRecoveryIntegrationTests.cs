using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class NotificationRecoveryIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public NotificationRecoveryIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }


    [Fact]
    public async Task Concurrent_WriteAsync_Calls_For_Same_Key_Produce_Exactly_One_Notification_And_Delivery()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        async Task WriteAsync(Guid id)
        {
            using var scope = _factory.Services.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
            await writer.WriteAsync(
                id, companyId, employeeId,
                "Leave approved", "Your leave request was approved.",
                sourceEntityId, NotificationType.LeaveApproved, NotificationPriority.Normal, now);
        }

        await Task.WhenAll(WriteAsync(idA), WriteAsync(idB));

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var notifications = await db.Notifications
            .Where(n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == NotificationType.LeaveApproved)
            .ToListAsync();
        var winningId = Assert.Single(notifications).Id;
        Assert.True(winningId == idA || winningId == idB);

        var deliveries = await db.EmailDeliveries.Where(d => d.NotificationId == winningId).ToListAsync();
        Assert.Single(deliveries);

        var auditDb = verifyScope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditEvents = await auditDb.AuditEvents
            .Where(e => e.EventId == winningId && e.EventType == "notifications.created")
            .ToListAsync();
        Assert.Single(auditEvents);

        var backgroundJobClient = Assert.IsType<FakeBackgroundJobClient>(
            verifyScope.ServiceProvider.GetRequiredService<Hangfire.IBackgroundJobClient>());
        Assert.Contains(backgroundJobClient.CreatedJobs, j => j.Args.ElementAtOrDefault(0) is Guid g && g == winningId);
    }


    [Fact]
    public async Task Running_ReconcileMissingNotificationAuditsJob_Twice_Produces_No_Duplicate_Audit_Rows()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-(ReconcileMissingNotificationAuditsJob.GraceMinutes + 5));

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.Add(Notification.Create(
                notificationId, companyId, employeeId, "Leave approved", null, Guid.NewGuid(), createdAt,
                NotificationType.LeaveApproved));
            await db.SaveChangesAsync();
        }

        async Task RunJobAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var job = ActivatorUtilities.CreateInstance<ReconcileMissingNotificationAuditsJob>(scope.ServiceProvider);
            await job.ExecuteAsync(CancellationToken.None);
        }

        await RunJobAsync();
        await RunJobAsync();

        using var verifyScope = _factory.Services.CreateScope();
        var auditDb = verifyScope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditEvents = await auditDb.AuditEvents
            .Where(e => e.EventId == notificationId && e.EventType == "notifications.created")
            .ToListAsync();
        Assert.Single(auditEvents);
    }

    [Fact]
    public async Task Running_ReconcilePendingEmailDeliveriesJob_Twice_Does_Not_Throw()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 5));

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.Add(Notification.Create(
                notificationId, companyId, employeeId, "Leave approved", null, Guid.NewGuid(), createdAt,
                NotificationType.LeaveApproved));
            db.EmailDeliveries.Add(EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, createdAt));
            await db.SaveChangesAsync();
        }

        async Task RunJobAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var job = ActivatorUtilities.CreateInstance<ReconcilePendingEmailDeliveriesJob>(scope.ServiceProvider);
            await job.ExecuteAsync(CancellationToken.None);
        }

        await RunJobAsync();
        await RunJobAsync(); // second run must not throw, even though the row is still Pending

        using var verifyScope = _factory.Services.CreateScope();
        var backgroundJobClient = Assert.IsType<FakeBackgroundJobClient>(
            verifyScope.ServiceProvider.GetRequiredService<Hangfire.IBackgroundJobClient>());
        Assert.Contains(backgroundJobClient.CreatedJobs, j => j.Args.ElementAtOrDefault(0) is Guid g && g == notificationId);
    }


    [Fact]
    public async Task ReconcilePendingEmailDeliveriesJob_Enqueues_A_Job_For_An_Orphaned_Pending_Delivery()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var oldEnough = DateTimeOffset.UtcNow.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 10));

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.Add(Notification.Create(
                notificationId, companyId, employeeId, "Leave approved", null, Guid.NewGuid(), oldEnough,
                NotificationType.LeaveApproved));
            db.EmailDeliveries.Add(EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, oldEnough));
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = ActivatorUtilities.CreateInstance<ReconcilePendingEmailDeliveriesJob>(scope.ServiceProvider);
            await job.ExecuteAsync(CancellationToken.None);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var backgroundJobClient = Assert.IsType<FakeBackgroundJobClient>(
            verifyScope.ServiceProvider.GetRequiredService<Hangfire.IBackgroundJobClient>());
        Assert.Contains(backgroundJobClient.CreatedJobs, j =>
            j.Type == typeof(EmailDeliveryJob)
            && (Guid?)j.Args.ElementAtOrDefault(0) == notificationId
            && (Guid?)j.Args.ElementAtOrDefault(1) == companyId);
    }


    [Fact]
    public async Task ReconcilePendingEmailDeliveriesJob_Never_ReEnqueues_A_Permanently_Failed_Delivery()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var oldEnough = DateTimeOffset.UtcNow.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 10));

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.Add(Notification.Create(
                notificationId, companyId, employeeId, "Leave approved", null, Guid.NewGuid(), oldEnough,
                NotificationType.LeaveApproved));
            var delivery = EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, oldEnough);
            delivery.RecordAttempt(oldEnough);
            delivery.MarkFailed("Invalid recipient address.");
            db.EmailDeliveries.Add(delivery);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = ActivatorUtilities.CreateInstance<ReconcilePendingEmailDeliveriesJob>(scope.ServiceProvider);
            await job.ExecuteAsync(CancellationToken.None);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var backgroundJobClient = Assert.IsType<FakeBackgroundJobClient>(
            verifyScope.ServiceProvider.GetRequiredService<Hangfire.IBackgroundJobClient>());
        Assert.DoesNotContain(backgroundJobClient.CreatedJobs, j => j.Args.ElementAtOrDefault(0) is Guid g && g == notificationId);
    }


    [Fact]
    public async Task ReconcilePendingEmailDeliveriesJob_Already_Cancelled_Token_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<ReconcilePendingEmailDeliveriesJob>(scope.ServiceProvider);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.ExecuteAsync(cts.Token));
    }

    [Fact]
    public async Task ReconcileMissingNotificationAuditsJob_Already_Cancelled_Token_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<ReconcileMissingNotificationAuditsJob>(scope.ServiceProvider);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.ExecuteAsync(cts.Token));
    }
}

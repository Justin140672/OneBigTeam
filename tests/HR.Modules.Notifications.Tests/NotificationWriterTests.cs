using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class NotificationWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsync_Persists_Notification()
    {
        await using var ctx  = BuildContext();
        var audit            = new FakeAuditPublisher();
        var writer           = new NotificationWriter(ctx, audit);
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

    [Fact]
    public async Task WriteAsync_Publishes_NotificationCreated_Audit_Event()
    {
        await using var ctx  = BuildContext();
        var audit            = new FakeAuditPublisher();
        var writer           = new NotificationWriter(ctx, audit);
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var sourceEntityId   = Guid.NewGuid();
        var id               = Guid.NewGuid();

        await writer.WriteAsync(
            id, companyId, employeeId,
            "Task assigned", null,
            sourceEntityId, NotificationType.TaskAssigned, NotificationPriority.High,
            Now);

        var evt     = Assert.Single(audit.Published);
        var created = Assert.IsType<NotificationCreatedAuditEvent>(evt);
        Assert.Equal(companyId,       created.CompanyId);
        Assert.Equal(id,              created.NotificationId);
        Assert.Equal(employeeId,      created.EmployeeId);
        Assert.Equal("Task assigned", created.Title);
        Assert.Equal("TaskAssigned",  created.Type);
        Assert.Equal("High",          created.Priority);
        Assert.Equal(sourceEntityId,  created.SourceEntityId);
        Assert.Equal(Now,             created.OccurredAt);
    }

    [Fact]
    public async Task ExistsAsync_Returns_True_When_Notification_Exists()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx, new FakeAuditPublisher());
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
        var writer          = new NotificationWriter(ctx, new FakeAuditPublisher());

        var exists = await writer.ExistsAsync(Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskDueSoon);
        Assert.False(exists);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}

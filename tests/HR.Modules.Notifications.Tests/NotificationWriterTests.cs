using HR.Modules.Notifications.Persistence;
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
        var writer           = new NotificationWriter(ctx);
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
    public async Task ExistsAsync_Returns_True_When_Notification_Exists()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx);
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
        var writer          = new NotificationWriter(ctx);

        var exists = await writer.ExistsAsync(Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskDueSoon);
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_When_Type_Does_Not_Match()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx);
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
        var writer           = new NotificationWriter(ctx);
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
        var writer          = new NotificationWriter(ctx);

        var result = await writer.GetLastSentAtAsync(Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskDueSoon);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLastSentAtAsync_Returns_Most_Recent_CreatedAt_When_Multiple_Exist()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx);
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
        var writer           = new NotificationWriter(ctx);
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
        var writer          = new NotificationWriter(ctx);

        var removed = await writer.RemoveBySourceEntityAsync(Guid.NewGuid(), Guid.NewGuid(), NotificationType.TaskDueSoon);
        Assert.Equal(0, removed);
    }

    [Fact]
    public async Task RemoveBySourceEntityAsync_Removes_Only_Matching_Notifications()
    {
        await using var ctx  = BuildContext();
        var writer           = new NotificationWriter(ctx);
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

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}

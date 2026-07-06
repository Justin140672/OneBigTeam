using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Tasks.Tests;

public class DueSoonNotifierTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private sealed record Harness(
        DueSoonNotifier Notifier,
        FakeNotificationWriter NotificationWriter,
        ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private static Harness BuildHarness(
        string dbName,
        FakeVacancyHiringManagerReader? hiringManagerReader = null)
    {
        var services = new ServiceCollection();

        services.AddDbContext<TasksDbContext>(options => options.UseInMemoryDatabase(dbName));

        var notificationWriter = new FakeNotificationWriter();
        services.AddSingleton<INotificationWriter>(notificationWriter);
        services.AddSingleton<IClock>(new FakeClock(FixedUtcNow));
        services.AddSingleton<IVacancyHiringManagerReader>(hiringManagerReader ?? new FakeVacancyHiringManagerReader());

        var provider = services.BuildServiceProvider();
        var notifier = new DueSoonNotifier(provider.GetRequiredService<IServiceScopeFactory>());

        return new Harness(notifier, notificationWriter, provider);
    }

    private static TaskItem CreateTask(
        Guid companyId,
        DateOnly dueDate,
        TaskSource source,
        TaskActionType actionType,
        Guid? assignedEmployeeId = null,
        Guid? sourceEntityId = null) =>
        TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Provide feedback: interview with Emma Clarke", null,
            TaskPriority.Medium, source, actionType,
            dueDate,
            assignedEmployeeId ?? Guid.NewGuid(),
            assignedEmployeeId ?? Guid.NewGuid(),
            Now,
            sourceEntityId);

    [Fact]
    public async Task CheckTaskAlertsAsync_Notifies_HiringManager_When_Interview_Feedback_Task_Overdue()
    {
        var companyId = Guid.NewGuid();
        var interviewId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        using var harness = BuildHarness(
            Guid.NewGuid().ToString("N"),
            new FakeVacancyHiringManagerReader(new Dictionary<Guid, Guid> { [interviewId] = hiringManagerId }));

        var task = CreateTask(
            companyId, Today.AddDays(-1),
            TaskSource.Recruitment, TaskActionType.Complete,
            sourceEntityId: interviewId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Contains(harness.NotificationWriter.Written, w =>
            w.EmployeeId == hiringManagerId &&
            w.Type == NotificationType.InterviewFeedbackOverdue &&
            w.SourceEntityId == task.Id);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Still_Notifies_Assigned_Interviewer_Of_Generic_Overdue()
    {
        var companyId = Guid.NewGuid();
        var interviewId = Guid.NewGuid();
        var interviewerId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        using var harness = BuildHarness(
            Guid.NewGuid().ToString("N"),
            new FakeVacancyHiringManagerReader(new Dictionary<Guid, Guid> { [interviewId] = hiringManagerId }));

        var task = CreateTask(
            companyId, Today.AddDays(-1),
            TaskSource.Recruitment, TaskActionType.Complete,
            assignedEmployeeId: interviewerId,
            sourceEntityId: interviewId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Contains(harness.NotificationWriter.Written, w =>
            w.EmployeeId == interviewerId &&
            w.Type == NotificationType.TaskOverdue);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Does_Not_Notify_HiringManager_When_Not_Overdue()
    {
        var companyId = Guid.NewGuid();
        var interviewId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        using var harness = BuildHarness(
            Guid.NewGuid().ToString("N"),
            new FakeVacancyHiringManagerReader(new Dictionary<Guid, Guid> { [interviewId] = hiringManagerId }));

        var task = CreateTask(
            companyId, Today.AddDays(1),
            TaskSource.Recruitment, TaskActionType.Complete,
            sourceEntityId: interviewId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.DoesNotContain(harness.NotificationWriter.Written, w => w.Type == NotificationType.InterviewFeedbackOverdue);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Does_Not_Notify_HiringManager_When_Task_Is_Not_Interview_Feedback()
    {
        var companyId = Guid.NewGuid();

        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today.AddDays(-1),
            TaskSource.Asset, TaskActionType.Acknowledge);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.DoesNotContain(harness.NotificationWriter.Written, w => w.Type == NotificationType.InterviewFeedbackOverdue);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Does_Not_Notify_HiringManager_When_Vacancy_Has_No_Resolvable_HiringManager()
    {
        var companyId = Guid.NewGuid();
        var interviewId = Guid.NewGuid();

        // No entry seeded in FakeVacancyHiringManagerReader for this interviewId.
        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today.AddDays(-1),
            TaskSource.Recruitment, TaskActionType.Complete,
            sourceEntityId: interviewId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.DoesNotContain(harness.NotificationWriter.Written, w => w.Type == NotificationType.InterviewFeedbackOverdue);
    }
}

using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests.Jobs;

// Ticket 4 (P1): unit tests for TaskCompletionEffectsJob, the durable, retryable worker that
// confirms the notification/audit side effects of a task completion whose business dispatch and
// TaskItem.Complete() transition were already committed by CompleteTaskHandler. Mirrors
// HR.Modules.Identity.Tests.Jobs.AccountDisablementJobTests's row-status idempotency pattern.
public class TaskCompletionEffectsJobTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(FixedNow);

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }

    private static TaskCompletionEffectsJob BuildJob(
        TasksDbContext context,
        FakeNotificationWriter? notif = null,
        FakeAuditPublisher? audit = null) =>
        new(context, notif ?? new FakeNotificationWriter(), Clock, audit ?? new FakeAuditPublisher(),
            NullLogger<TaskCompletionEffectsJob>.Instance);

    private static TaskItem MakeCompletedTask(Guid companyId, Guid? assignedEmployeeId, Guid completedBy)
    {
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Onboarding checklist", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployeeId, null, DateTimeOffset.UtcNow);
        task.Complete(completedBy, DateTimeOffset.UtcNow);
        return task;
    }

    private static async Task<(Guid CompanyId, Guid EmployeeId, Guid CompletedBy, TaskItem Task, TaskCompletionOperation Operation)>
        SeedDispatchAppliedOperationAsync(TasksDbContext context)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        var task = MakeCompletedTask(companyId, employeeId, completedBy);
        context.TaskItems.Add(task);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, null, null, DateTimeOffset.UtcNow);
        operation.MarkDispatchApplied(DateTimeOffset.UtcNow);
        context.TaskCompletionOperations.Add(operation);

        await context.SaveChangesAsync();
        return (companyId, employeeId, completedBy, task, operation);
    }

    [Fact]
    public async Task ProcessAsync_Is_NoOp_When_Operation_Is_Missing()
    {
        await using var context = BuildContext();
        var audit = new FakeAuditPublisher();
        var job = BuildJob(context, audit: audit);

        var exception = await Record.ExceptionAsync(
            () => job.ProcessAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Null(exception);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task ProcessAsync_Throws_When_CompanyId_Does_Not_Match_Operation()
    {
        await using var context = BuildContext();
        var (companyId, _, _, _, operation) = await SeedDispatchAppliedOperationAsync(context);
        var otherCompanyId = Guid.NewGuid();
        Assert.NotEqual(companyId, otherCompanyId);

        var audit = new FakeAuditPublisher();
        var job = BuildJob(context, audit: audit);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.ProcessAsync(operation.Id, otherCompanyId));

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task ProcessAsync_Is_NoOp_When_Already_Processed()
    {
        await using var context = BuildContext();
        var (companyId, _, _, _, operation) = await SeedDispatchAppliedOperationAsync(context);
        operation.MarkProcessed(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        var notif = new FakeNotificationWriter();
        var audit = new FakeAuditPublisher();
        var job = BuildJob(context, notif, audit);

        await job.ProcessAsync(operation.Id, companyId);

        Assert.Empty(notif.Written);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task ProcessAsync_Marks_Processed_Without_Side_Effects_When_TaskItem_No_Longer_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();

        // No TaskItem seeded — it no longer exists.
        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, Guid.NewGuid(), completedBy, null, null, DateTimeOffset.UtcNow);
        operation.MarkDispatchApplied(DateTimeOffset.UtcNow);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var notif = new FakeNotificationWriter();
        var audit = new FakeAuditPublisher();
        var job = BuildJob(context, notif, audit);

        await job.ProcessAsync(operation.Id, companyId);

        var reloaded = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, reloaded.Status);
        Assert.NotNull(reloaded.ProcessedAt);
        Assert.Empty(notif.Written);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task ProcessAsync_Writes_Notification_Publishes_Audit_And_Marks_Processed_When_Notification_Not_Already_Sent()
    {
        await using var context = BuildContext();
        var (companyId, employeeId, completedBy, task, operation) = await SeedDispatchAppliedOperationAsync(context);

        var notif = new FakeNotificationWriter();
        var audit = new FakeAuditPublisher();
        var job = BuildJob(context, notif, audit);

        await job.ProcessAsync(operation.Id, companyId);

        var written = Assert.Single(notif.Written);
        Assert.Equal(employeeId, written.EmployeeId);
        Assert.Equal(task.Id, written.SourceEntityId);
        Assert.Equal(NotificationType.TaskCompleted, written.Type);

        Assert.Single(audit.Published);

        var reloaded = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, reloaded.Status);
        Assert.NotNull(reloaded.ProcessedAt);
        Assert.Equal(1, reloaded.AttemptCount);
    }

    [Fact]
    public async Task ProcessAsync_Skips_Duplicate_Notification_Write_But_Still_Publishes_Audit_And_Marks_Processed_When_Notification_Already_Exists()
    {
        await using var context = BuildContext();
        var (companyId, employeeId, completedBy, task, operation) = await SeedDispatchAppliedOperationAsync(context);

        // Simulate a prior attempt that already wrote the notification (e.g. the inline write in
        // CompleteTaskHandler actually succeeded, but the subsequent audit publish failed) —
        // ExistsAsync now returns true for this (employeeId, taskId, TaskCompleted) triple.
        var notif = new FakeNotificationWriter();
        await notif.WriteAsync(
            Guid.NewGuid(), companyId, employeeId, "Task completed: Onboarding checklist", null,
            task.Id, NotificationType.TaskCompleted, NotificationPriority.Normal, DateTimeOffset.UtcNow);
        var preExistingCount = notif.Written.Count;

        var audit = new FakeAuditPublisher();
        var job = BuildJob(context, notif, audit);

        await job.ProcessAsync(operation.Id, companyId);

        // No duplicate write — count unchanged.
        Assert.Equal(preExistingCount, notif.Written.Count);

        // Audit is still published on a retry (no natural dedupe key for it).
        Assert.Single(audit.Published);

        var reloaded = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusProcessed, reloaded.Status);
    }

    [Fact]
    public async Task ProcessAsync_Records_Attempt_And_Failure_And_Rethrows_On_Transient_Error()
    {
        await using var context = BuildContext();
        var (companyId, _, _, _, operation) = await SeedDispatchAppliedOperationAsync(context);

        var audit = new FakeAuditPublisher { ThrowOnPublish = true };
        var job = BuildJob(context, audit: audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(operation.Id, companyId));

        var reloaded = await context.TaskCompletionOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(TaskCompletionOperation.StatusDispatchApplied, reloaded.Status);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Equal("Simulated audit publish failure.", reloaded.FailureReason);
        Assert.Null(reloaded.ProcessedAt);
    }
}

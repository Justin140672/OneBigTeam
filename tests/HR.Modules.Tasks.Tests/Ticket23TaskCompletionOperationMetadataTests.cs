using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Tasks.Tests;

/// <summary>
/// Ticket 23 (P2): durable correlation/causation/message-id metadata on
/// <see cref="TaskCompletionOperation"/> — extends the pattern already established for
/// HR.Modules.Employees (the reference module; see
/// HR.Modules.Employees.Tests.AuditOutboxMetadataTests) to the Tasks module's own durable
/// operation record and its owning job (<see cref="TaskCompletionEffectsJob"/>).
/// </summary>
public class Ticket23TaskCompletionOperationMetadataTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private static readonly TaskCompletionDispatcher NoOpDispatcher =
        new(Enumerable.Empty<ITaskCompletionAction>());

    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private static TasksDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    // ── CreatePending: stamping from a supplied IExecutionContext ───────────────────────────────

    [Fact]
    public void CreatePending_With_Supplied_Context_Stamps_CorrelationId_From_Context_CorrelationId_And_CausationId_From_Context_MessageId()
    {
        var context = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "approve", null, Now, context);

        Assert.Equal(context.MessageId, operation.CorrelationId);
        Assert.Equal(context.MessageId, operation.CausationId);
        Assert.NotNull(operation.MessageId);
        Assert.NotEqual(Guid.Empty, operation.MessageId!.Value);
        Assert.NotEqual(context.MessageId, operation.MessageId!.Value);
    }

    [Fact]
    public void CreatePending_With_No_Context_Leaves_Correlation_And_Causation_Null_But_Still_Mints_A_Fresh_MessageId()
    {
        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "approve", null, Now);

        Assert.Null(operation.CorrelationId);
        Assert.Null(operation.CausationId);
        Assert.NotNull(operation.MessageId);
        Assert.NotEqual(Guid.Empty, operation.MessageId!.Value);
    }

    // ── CompleteTaskHandler: reference wiring picks up the ambient context ──────────────────────

    private static CompleteTaskHandler BuildHandler(
        TasksDbContext context,
        IExecutionContextAccessor? executionContextAccessor,
        RecordingBackgroundJobClient? backgroundJobClient = null) =>
        new(context, new FakeNotificationWriter(), Clock, new FakeAuditPublisher(), NoOpDispatcher,
            new TasksResourceAuthorizer(
                new FakeRoleAuthorizationService(HrAdministratorRoleId), new FakeDirectReportsReader()),
            backgroundJobClient ?? new RecordingBackgroundJobClient(),
            NullLogger<CompleteTaskHandler>.Instance,
            executionContextAccessor);

    private static TaskItem MakeOpenTask(Guid companyId) =>
        TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task HandleAsync_With_Ambient_Context_Stamps_The_Created_Operations_Correlation_And_Causation_From_It()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var task = MakeOpenTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var ambient = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        using (accessor.Push(ambient))
        {
            var result = await BuildHandler(context, accessor).HandleAsync(
                new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = Guid.NewGuid() },
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        var operation = await context.TaskCompletionOperations.SingleAsync(o => o.TaskId == task.Id);
        Assert.Equal(ambient.MessageId, operation.CorrelationId);
        Assert.Equal(ambient.MessageId, operation.CausationId);
    }

    [Fact]
    public async Task HandleAsync_With_No_Accessor_Leaves_The_Created_Operations_Metadata_Null_Except_MessageId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var task = MakeOpenTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context, executionContextAccessor: null).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var operation = await context.TaskCompletionOperations.SingleAsync(o => o.TaskId == task.Id);
        Assert.Null(operation.CorrelationId);
        Assert.Null(operation.CausationId);
        Assert.NotNull(operation.MessageId);
    }

    // ── TaskCompletionEffectsJob: restores persisted metadata as the ambient context ────────────

    // Captures whatever execution context is ambient (via the supplied accessor) at the moment the
    // audit event is about to be published — used to observe what the job restored as ambient for
    // the duration of its resumed work.
    private sealed class ContextCapturingAuditPublisher(IExecutionContextAccessor accessor) : IAuditEventPublisher
    {
        public IExecutionContext? ObservedDuringPublish { get; private set; }

        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            ObservedDuringPublish = accessor.Current;
            return Task.CompletedTask;
        }
    }

    private static TaskItem MakeCompletedTask(Guid companyId, Guid completedBy)
    {
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, null, DateTimeOffset.UtcNow);
        task.Complete(completedBy, DateTimeOffset.UtcNow);
        return task;
    }

    [Fact]
    public async Task ProcessAsync_Restores_Persisted_CorrelationId_CausationId_And_MessageId_As_The_Ambient_Context()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var task = MakeCompletedTask(companyId, completedBy);
        context.TaskItems.Add(task);

        var executionContext = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, null, null, Now, executionContext);
        operation.MarkDispatchApplied(Now);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var auditPublisher = new ContextCapturingAuditPublisher(accessor);
        var job = new TaskCompletionEffectsJob(
            context, new FakeNotificationWriter(), Clock, auditPublisher,
            NullLogger<TaskCompletionEffectsJob>.Instance, accessor);

        await job.ProcessAsync(operation.Id, companyId);

        Assert.NotNull(auditPublisher.ObservedDuringPublish);
        var restored = auditPublisher.ObservedDuringPublish!;
        Assert.Equal(executionContext.CorrelationId, restored.CorrelationId);
        Assert.Equal(operation.CausationId, restored.CausationId);
        Assert.Equal(operation.MessageId, restored.MessageId);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, restored.Origin);
    }

    [Fact]
    public async Task ProcessAsync_Legacy_Row_With_Null_Metadata_Still_Restores_A_Fresh_Root_Context_Rather_Than_Throwing()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var task = MakeCompletedTask(companyId, completedBy);
        context.TaskItems.Add(task);

        // Simulate a row written before the metadata columns existed — no supplied context.
        var operation = TaskCompletionOperation.CreatePending(
            Guid.NewGuid(), companyId, task.Id, completedBy, null, null, Now);
        operation.MarkDispatchApplied(Now);
        context.TaskCompletionOperations.Add(operation);
        await context.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var auditPublisher = new ContextCapturingAuditPublisher(accessor);
        var job = new TaskCompletionEffectsJob(
            context, new FakeNotificationWriter(), Clock, auditPublisher,
            NullLogger<TaskCompletionEffectsJob>.Instance, accessor);

        var exception = await Record.ExceptionAsync(() => job.ProcessAsync(operation.Id, companyId));

        Assert.Null(exception);
        Assert.NotNull(auditPublisher.ObservedDuringPublish);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, auditPublisher.ObservedDuringPublish!.Origin);
        Assert.Null(auditPublisher.ObservedDuringPublish.CausationId);
    }
}

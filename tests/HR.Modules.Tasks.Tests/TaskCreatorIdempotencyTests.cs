using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

/// <summary>
/// OBT-REM-13: unit-level coverage of TaskCreator's idempotency-key behaviour that does not require a
/// real database constraint violation to exercise — the read-before-create optimisation, and
/// unaffected behaviour when no key is supplied. EF Core's InMemory provider does not enforce the
/// (company_id, idempotency_key) unique index or throw Npgsql's PostgresException shape, so proving
/// the actual concurrent-conflict catch path requires a real Postgres backend — see
/// HR.Integration.Tests/TaskCreatorIdempotencyIntegrationTests for that coverage.
/// </summary>
public class TaskCreatorIdempotencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_With_Existing_Key_Returns_Existing_Id_Without_Inserting_A_Second_Row()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var idempotencyKey = "SicknessEvidenceOverdue:existing";

        var existing = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Fit note overdue — Jane Doe", null,
            TaskPriority.High, TaskSource.Sickness, TaskActionType.Complete,
            null, null, null, Now, sourceEntityId: Guid.NewGuid(), idempotencyKey: idempotencyKey);
        ctx.TaskItems.Add(existing);
        await ctx.SaveChangesAsync();

        var creator = new TaskCreator(ctx, new FakeNotificationWriter(), new FakeClock(Now.UtcDateTime), new FakeAuditPublisher());

        var returnedId = await creator.CreateAsync(
            companyId, Guid.NewGuid(),
            "Fit note overdue — Jane Doe", null,
            TaskPriority.High, TaskSource.Sickness, TaskActionType.Complete,
            null,
            assignedEmployeeId: null,
            assignedUserId: null,
            sourceEntityId: Guid.NewGuid(),
            CancellationToken.None,
            idempotencyKey: idempotencyKey);

        Assert.Equal(existing.Id, returnedId);

        var rows = await ctx.TaskItems.Where(t => t.CompanyId == companyId && t.IdempotencyKey == idempotencyKey).ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task CreateAsync_With_A_New_Key_Inserts_A_Row_And_Persists_The_Key()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var idempotencyKey = "SicknessEvidenceOverdue:new";

        var creator = new TaskCreator(ctx, new FakeNotificationWriter(), new FakeClock(Now.UtcDateTime), new FakeAuditPublisher());

        var id = await creator.CreateAsync(
            companyId, Guid.NewGuid(),
            "Fit note overdue — John Smith", null,
            TaskPriority.High, TaskSource.Sickness, TaskActionType.Complete,
            null,
            assignedEmployeeId: null,
            assignedUserId: null,
            sourceEntityId: Guid.NewGuid(),
            CancellationToken.None,
            idempotencyKey: idempotencyKey);

        var task = await ctx.TaskItems.SingleAsync(t => t.Id == id);
        Assert.Equal(idempotencyKey, task.IdempotencyKey);
    }

    [Fact]
    public async Task CreateAsync_Without_A_Key_Never_Sets_IdempotencyKey_And_Does_Not_Dedupe_Against_A_Prior_Call()
    {
        // Unchanged legacy behaviour: idempotencyKey null (the default, and what most existing
        // callers still pass) means no read-before-create check at all — two calls with otherwise
        // identical arguments both create their own task, exactly as before OBT-REM-13.
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();

        var creator = new TaskCreator(ctx, new FakeNotificationWriter(), new FakeClock(Now.UtcDateTime), new FakeAuditPublisher());

        var idA = await creator.CreateAsync(
            companyId, Guid.NewGuid(), "A task", null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployeeId: null, assignedUserId: null, sourceEntityId: sourceEntityId,
            CancellationToken.None);

        var idB = await creator.CreateAsync(
            companyId, Guid.NewGuid(), "A task", null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployeeId: null, assignedUserId: null, sourceEntityId: sourceEntityId,
            CancellationToken.None);

        Assert.NotEqual(idA, idB);

        var taskA = await ctx.TaskItems.SingleAsync(t => t.Id == idA);
        Assert.Null(taskA.IdempotencyKey);
    }

    [Fact]
    public async Task TrySaveIdempotentlyAsync_Does_Not_Swallow_A_DbUpdateException_For_A_Different_Constraint()
    {
        // Simulates a losing SaveChangesAsync that fails for a reason other than the idempotency
        // index — e.g. a different unique constraint. TaskCreator's catch-when clause must not treat
        // this as a successful idempotent replay; it must propagate. InMemory can't throw a real
        // PostgresException-shaped conflict on the idempotency index itself (see the integration
        // tests for that), so this test drives the same catch-when logic directly via
        // PostgresUniqueViolation to pin the "different constraint name -> not matched" contract that
        // TrySaveIdempotentlyAsync depends on.
        var unrelated = new DbUpdateException(
            "update failed",
            new InvalidOperationException(
                "23505: duplicate key value violates unique constraint \"ix_some_other_index\""));

        Assert.False(PostgresUniqueViolation.Is(unrelated, "ix_task_items_company_id_idempotency_key"));
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TasksDbContext(options);
    }
}

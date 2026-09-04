using HR.Integration.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// OBT-REM-13: end-to-end coverage of TaskCreator's idempotency-key path that specifically needs a
/// real PostgreSQL backend — unlike EF Core's InMemory provider, only a real Postgres testcontainer
/// enforces the partial unique index on (company_id, idempotency_key)
/// ("ix_task_items_company_id_idempotency_key") and surfaces Npgsql's PostgresException shape, which
/// is what actually drives TaskCreator.TrySaveIdempotentlyAsync's duplicate-detection catch clause.
/// See HR.Modules.Tasks.Tests for isolated unit-level coverage of the non-conflict-path logic.
/// </summary>
[Collection("Integration")]
public class TaskCreatorIdempotencyIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public TaskCreatorIdempotencyIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<Guid> CreateAsync(
        Guid companyId,
        string idempotencyKey,
        Guid? sourceEntityId = null,
        string title = "Fit note overdue")
    {
        using var scope = _factory.Services.CreateScope();
        var creator = scope.ServiceProvider.GetRequiredService<ITaskCreator>();
        return await creator.CreateAsync(
            companyId, Guid.NewGuid(),
            title, "Overdue fit note evidence",
            TaskPriority.High, TaskSource.Sickness, TaskActionType.Complete,
            dueDate: null,
            assignedEmployeeId: null,
            assignedUserId: null,
            sourceEntityId: sourceEntityId,
            CancellationToken.None,
            idempotencyKey: idempotencyKey);
    }

    // 1. Two concurrent handlers processing the same event -----------------------------------------

    [Fact]
    public async Task Concurrent_CreateAsync_Calls_For_Same_Key_Produce_Exactly_One_Task()
    {
        var companyId = Guid.NewGuid();
        var evidenceRequestId = Guid.NewGuid();
        var idempotencyKey = $"SicknessEvidenceOverdue:{evidenceRequestId}";

        var (idA, idB) = await WhenAllAsync(
            CreateAsync(companyId, idempotencyKey, evidenceRequestId),
            CreateAsync(companyId, idempotencyKey, evidenceRequestId));

        Assert.Equal(idA, idB);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var tasks = await db.TaskItems
            .Where(t => t.CompanyId == companyId && t.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        Assert.Single(tasks);
        Assert.Equal(idA, tasks[0].Id);
    }

    // 2. Sequential replay of the same event ---------------------------------------------------------

    [Fact]
    public async Task Sequential_CreateAsync_Calls_For_Same_Key_Return_Same_Id_And_Create_No_Second_Task()
    {
        var companyId = Guid.NewGuid();
        var evidenceRequestId = Guid.NewGuid();
        var idempotencyKey = $"SicknessEvidenceOverdue:{evidenceRequestId}";

        var firstId = await CreateAsync(companyId, idempotencyKey, evidenceRequestId);
        var secondId = await CreateAsync(companyId, idempotencyKey, evidenceRequestId);

        Assert.Equal(firstId, secondId);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var tasks = await db.TaskItems
            .Where(t => t.CompanyId == companyId && t.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        Assert.Single(tasks);
    }

    // 3. Same idempotency key in different companies -> no false collision --------------------------

    [Fact]
    public async Task Same_Idempotency_Key_In_Different_Companies_Creates_A_Task_Per_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var evidenceRequestId = Guid.NewGuid();
        var idempotencyKey = $"SicknessEvidenceOverdue:{evidenceRequestId}";

        var idA = await CreateAsync(companyA, idempotencyKey, evidenceRequestId);
        var idB = await CreateAsync(companyB, idempotencyKey, evidenceRequestId);

        Assert.NotEqual(idA, idB);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var tasks = await db.TaskItems
            .Where(t => t.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, t => t.CompanyId == companyA && t.Id == idA);
        Assert.Contains(tasks, t => t.CompanyId == companyB && t.Id == idB);
    }

    // 4. Different workflow keys against the same source entity -> both tasks created ---------------

    [Fact]
    public async Task Different_Idempotency_Keys_For_Same_Company_Both_Create_Tasks()
    {
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var keyA = $"SicknessEvidenceOverdue:{sourceEntityId}";
        var keyB = $"SicknessFitNoteThreshold:{sourceEntityId}";

        var idA = await CreateAsync(companyId, keyA, sourceEntityId);
        var idB = await CreateAsync(companyId, keyB, sourceEntityId);

        Assert.NotEqual(idA, idB);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var tasks = await db.TaskItems
            .Where(t => t.CompanyId == companyId && t.SourceEntityId == sourceEntityId)
            .ToListAsync();

        Assert.Equal(2, tasks.Count);
    }

    private static async Task<(Guid, Guid)> WhenAllAsync(Task<Guid> a, Task<Guid> b)
    {
        await Task.WhenAll(a, b);
        return (a.Result, b.Result);
    }
}

using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Features.SicknessEvidenceOverdue;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests;

public class NotifyHrOfOverdueFitNoteHandlerTests
{
    private static readonly Guid CompanyId         = Guid.NewGuid();
    private static readonly Guid EmployeeId        = Guid.NewGuid();
    private static readonly Guid SicknessRecordId  = Guid.NewGuid();
    private static readonly Guid EvidenceRequestId = Guid.NewGuid();
    private static readonly DateOnly DueDate       = new(2026, 7, 9);

    private static SicknessEvidenceOverdueIntegrationEvent MakeEvent(
        Guid? companyId = null,
        Guid? employeeId = null,
        Guid? evidenceRequestId = null,
        DateOnly? dueDate = null) =>
        new(
            CompanyId:        companyId        ?? CompanyId,
            EmployeeId:       employeeId       ?? EmployeeId,
            SicknessRecordId: SicknessRecordId,
            EvidenceRequestId: evidenceRequestId ?? EvidenceRequestId,
            DueDate:          dueDate          ?? DueDate,
            OccurredAt:       DateTimeOffset.UtcNow);

    private static NotifyHrOfOverdueFitNoteHandler BuildHandler(
        FakeTaskCreator creator,
        FakeEmployeeNameReader? employeeNameReader = null) =>
        new(creator, new FakeOpenTaskBySourceEntityReader(), employeeNameReader ?? new FakeEmployeeNameReader());

    [Fact]
    public async Task HandleAsync_Creates_One_Task()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Single(creator.Created);
    }

    [Fact]
    public async Task HandleAsync_Task_Is_Unassigned()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Null(creator.Created[0].AssignedEmployeeId);
        Assert.Null(creator.Created[0].AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_Title_Includes_Employee_Name()
    {
        var creator = new FakeTaskCreator();
        var employeeNameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [EmployeeId] = "Jane Doe" });
        var handler = BuildHandler(creator, employeeNameReader);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal("Fit note overdue — Jane Doe", creator.Created[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Source_Is_Sickness()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskSource.Sickness, creator.Created[0].Source);
    }

    [Fact]
    public async Task HandleAsync_ActionType_Is_Complete()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        // Not TaskActionType.Review — reserved for return-to-work review tasks (see
        // NotifyHrOfFitNoteThresholdHandlerTests for the same note).
        Assert.Equal(TaskActionType.Complete, creator.Created[0].ActionType);
    }

    [Fact]
    public async Task HandleAsync_Priority_Is_High()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskPriority.High, creator.Created[0].Priority);
    }

    [Fact]
    public async Task HandleAsync_DueDate_Matches_Event()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);
        var dueDate = new DateOnly(2026, 8, 15);

        await handler.HandleAsync(MakeEvent(dueDate: dueDate), CancellationToken.None);

        Assert.Equal(dueDate, creator.Created[0].DueDate);
    }

    [Fact]
    public async Task HandleAsync_CompanyId_Matches_Event()
    {
        var creator   = new FakeTaskCreator();
        var handler   = BuildHandler(creator);
        var companyId = Guid.NewGuid();

        await handler.HandleAsync(MakeEvent(companyId: companyId), CancellationToken.None);

        Assert.Equal(companyId, creator.Created[0].CompanyId);
    }

    [Fact]
    public async Task HandleAsync_SourceEntityId_Is_EvidenceRequestId()
    {
        var creator           = new FakeTaskCreator();
        var handler           = BuildHandler(creator);
        var evidenceRequestId = Guid.NewGuid();

        await handler.HandleAsync(MakeEvent(evidenceRequestId: evidenceRequestId), CancellationToken.None);

        Assert.Equal(evidenceRequestId, creator.Created[0].SourceEntityId);
    }

    [Fact]
    public async Task HandleAsync_Description_Mentions_Overdue()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Contains("overdue", creator.Created[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    // OBT-REM-13: idempotency-key wiring ------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Passes_A_Deterministic_IdempotencyKey_Derived_From_EvidenceRequestId()
    {
        var creator           = new FakeTaskCreator();
        var handler           = BuildHandler(creator);
        var evidenceRequestId = Guid.NewGuid();

        await handler.HandleAsync(MakeEvent(evidenceRequestId: evidenceRequestId), CancellationToken.None);

        Assert.Equal($"SicknessEvidenceOverdue:{evidenceRequestId}", creator.Created[0].IdempotencyKey);
    }

    [Fact]
    public async Task HandleAsync_Called_Twice_For_Same_Event_Passes_The_Same_IdempotencyKey_Both_Times()
    {
        // Sequential replay of the same underlying occurrence (e.g. a retried Hangfire delivery) must
        // produce two CreateAsync calls with an identical idempotency key — correctness against the
        // resulting duplicate is then TaskCreator's/the database's responsibility (see
        // TaskCreatorIdempotencyTests and TaskCreatorIdempotencyIntegrationTests), not this handler's.
        var creator           = new FakeTaskCreator();
        var handler           = BuildHandler(creator);
        var evidenceRequestId = Guid.NewGuid();
        var evt                = MakeEvent(evidenceRequestId: evidenceRequestId);

        await handler.HandleAsync(evt, CancellationToken.None);
        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Equal(2, creator.Created.Count);
        Assert.Equal(creator.Created[0].IdempotencyKey, creator.Created[1].IdempotencyKey);
        Assert.Equal($"SicknessEvidenceOverdue:{evidenceRequestId}", creator.Created[1].IdempotencyKey);
    }
}

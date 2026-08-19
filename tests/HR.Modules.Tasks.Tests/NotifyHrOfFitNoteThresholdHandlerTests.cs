using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Features.SicknessEvidenceRequested;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests;

public class NotifyHrOfFitNoteThresholdHandlerTests
{
    private static readonly Guid CompanyId        = Guid.NewGuid();
    private static readonly Guid EmployeeId       = Guid.NewGuid();
    private static readonly Guid SicknessRecordId = Guid.NewGuid();
    private static readonly Guid EvidenceRequestId = Guid.NewGuid();
    private static readonly DateOnly DueDate       = new(2026, 7, 9);

    private static SicknessEvidenceRequestedIntegrationEvent MakeEvent(
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

    private static NotifyHrOfFitNoteThresholdHandler BuildHandler(
        FakeTaskCreator creator,
        FakeEmployeeNameReader? employeeNameReader = null) =>
        new(creator, employeeNameReader ?? new FakeEmployeeNameReader());

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

        Assert.Equal("Fit note required — Jane Doe", creator.Created[0].Title);
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

        // Not TaskActionType.Review — that combo (Source=Sickness, ActionType=Review) is reserved
        // for return-to-work review tasks in TaskView.razor's dispatch; this task's SourceEntityId
        // is a SicknessEvidenceRequest.Id, not a ReturnToWorkReview.Id, so reusing Review would
        // route it to the wrong panel in the UI.
        Assert.Equal(TaskActionType.Complete, creator.Created[0].ActionType);
    }

    [Fact]
    public async Task HandleAsync_Priority_Is_Medium()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskPriority.Medium, creator.Created[0].Priority);
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
    public async Task HandleAsync_Description_Mentions_Fit_Note_Threshold()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Contains("fit note", creator.Created[0].Description, StringComparison.OrdinalIgnoreCase);
    }
}

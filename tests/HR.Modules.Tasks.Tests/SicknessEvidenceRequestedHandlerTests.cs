using HR.Modules.Tasks.Features.SicknessEvidenceRequested;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests;

public class SicknessEvidenceRequestedHandlerTests
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

    [Fact]
    public async Task HandleAsync_Creates_One_Task()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Single(creator.Created);
    }

    [Fact]
    public async Task HandleAsync_Title_Is_Upload_Fit_Note()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal("Upload fit note", creator.Created[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Source_Is_Sickness()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskSource.Sickness, creator.Created[0].Source);
    }

    [Fact]
    public async Task HandleAsync_ActionType_Is_Upload()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskActionType.Upload, creator.Created[0].ActionType);
    }

    [Fact]
    public async Task HandleAsync_Priority_Is_Medium()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskPriority.Medium, creator.Created[0].Priority);
    }

    [Fact]
    public async Task HandleAsync_AssignedTo_Employee()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(EmployeeId, creator.Created[0].AssignedEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_AssignedUserId_Is_Null()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Null(creator.Created[0].AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_DueDate_Matches_Event()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);
        var dueDate = new DateOnly(2026, 8, 15);

        await handler.HandleAsync(MakeEvent(dueDate: dueDate), CancellationToken.None);

        Assert.Equal(dueDate, creator.Created[0].DueDate);
    }

    [Fact]
    public async Task HandleAsync_CompanyId_Matches_Event()
    {
        var creator   = new FakeTaskCreator();
        var handler   = new SicknessEvidenceRequestedHandler(creator);
        var companyId = Guid.NewGuid();

        await handler.HandleAsync(MakeEvent(companyId: companyId), CancellationToken.None);

        Assert.Equal(companyId, creator.Created[0].CompanyId);
    }

    [Fact]
    public async Task HandleAsync_SourceEntityId_Is_EvidenceRequestId()
    {
        var creator           = new FakeTaskCreator();
        var handler           = new SicknessEvidenceRequestedHandler(creator);
        var evidenceRequestId = Guid.NewGuid();

        await handler.HandleAsync(MakeEvent(evidenceRequestId: evidenceRequestId), CancellationToken.None);

        Assert.Equal(evidenceRequestId, creator.Created[0].SourceEntityId);
    }

    [Fact]
    public async Task HandleAsync_Description_Mentions_Fit_Note()
    {
        var creator = new FakeTaskCreator();
        var handler = new SicknessEvidenceRequestedHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Contains("fit note", creator.Created[0].Description, StringComparison.OrdinalIgnoreCase);
    }
}

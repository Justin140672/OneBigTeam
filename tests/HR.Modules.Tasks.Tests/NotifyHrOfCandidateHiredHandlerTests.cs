using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Features.CandidateHired;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests;

public class NotifyHrOfCandidateHiredHandlerTests
{
    private static readonly Guid CompanyId     = Guid.NewGuid();
    private static readonly Guid ApplicationId = Guid.NewGuid();
    private static readonly Guid CandidateId   = Guid.NewGuid();
    private static readonly Guid EmployeeId    = Guid.NewGuid();
    private static readonly Guid VacancyId     = Guid.NewGuid();

    private static CandidateHiredIntegrationEvent MakeEvent(
        Guid? companyId = null,
        Guid? applicationId = null,
        Guid? employeeId = null) =>
        new(
            CompanyId:     companyId     ?? CompanyId,
            ApplicationId: applicationId ?? ApplicationId,
            CandidateId:   CandidateId,
            EmployeeId:    employeeId    ?? EmployeeId,
            VacancyId:     VacancyId,
            OccurredAt:    DateTimeOffset.UtcNow);

    private static NotifyHrOfCandidateHiredHandler BuildHandler(
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
        var employeeNameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [EmployeeId] = "Olivia Grant" });
        var handler = BuildHandler(creator, employeeNameReader);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal("Candidate hired — Olivia Grant", creator.Created[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Source_Is_Recruitment()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskSource.Recruitment, creator.Created[0].Source);
    }

    [Fact]
    public async Task HandleAsync_ActionType_Is_Review()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        // Not TaskActionType.Complete — that value is reserved for the interview feedback
        // completion action (InterviewFeedbackTaskCompletionAction) and must not be triggered
        // by completing this unrelated HR inbox task.
        Assert.Equal(TaskActionType.Review, creator.Created[0].ActionType);
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
    public async Task HandleAsync_SourceEntityId_Is_ApplicationId()
    {
        var creator       = new FakeTaskCreator();
        var handler       = BuildHandler(creator);
        var applicationId = Guid.NewGuid();

        await handler.HandleAsync(MakeEvent(applicationId: applicationId), CancellationToken.None);

        Assert.Equal(applicationId, creator.Created[0].SourceEntityId);
    }

    [Fact]
    public async Task HandleAsync_Description_Mentions_Hired()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Contains("hired", creator.Created[0].Description, StringComparison.OrdinalIgnoreCase);
    }
}

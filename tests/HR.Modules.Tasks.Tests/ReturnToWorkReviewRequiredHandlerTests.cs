using HR.Modules.Tasks.Features.ReturnToWorkReviewRequired;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests;

public class ReturnToWorkReviewRequiredHandlerTests
{
    private static readonly Guid CompanyId        = Guid.NewGuid();
    private static readonly Guid EmployeeId       = Guid.NewGuid();
    private static readonly Guid SicknessRecordId = Guid.NewGuid();
    private static readonly Guid ReviewId         = Guid.NewGuid();
    private static readonly Guid ManagerId        = Guid.NewGuid();
    private static readonly DateOnly DueDate      = new(2026, 7, 9);

    private static ReturnToWorkReviewRequiredIntegrationEvent MakeEvent(
        Guid? companyId = null,
        Guid? employeeId = null,
        Guid? reviewId = null,
        DateOnly? dueDate = null) =>
        new(
            CompanyId:        companyId ?? CompanyId,
            EmployeeId:       employeeId ?? EmployeeId,
            SicknessRecordId: SicknessRecordId,
            ReviewId:         reviewId ?? ReviewId,
            DueDate:          dueDate ?? DueDate,
            OccurredAt:       DateTimeOffset.UtcNow);

    private static ReturnToWorkReviewRequiredHandler BuildHandler(
        FakeTaskCreator taskCreator,
        Guid? managerId = null,
        string? employeeName = null)
    {
        var names = employeeName is not null
            ? new Dictionary<Guid, string> { [EmployeeId] = employeeName }
            : null;

        return new ReturnToWorkReviewRequiredHandler(
            taskCreator,
            new FakeEmployeeNameReader(names),
            new FakeManagerReader(managerId));
    }

    [Fact]
    public async Task HandleAsync_Creates_One_Task()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator, managerId: ManagerId, employeeName: "Alice Smith");

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Single(creator.Created);
    }

    [Fact]
    public async Task HandleAsync_Title_Includes_Employee_Name()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator, employeeName: "Alice Smith");

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal("Return-to-work review — Alice Smith", creator.Created[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Title_Falls_Back_When_Name_Unknown()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator, employeeName: null);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal("Return-to-work review — Unknown Employee", creator.Created[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Assigns_To_Manager_When_Employee_Has_One()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator, managerId: ManagerId);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(ManagerId, creator.Created[0].AssignedEmployeeId);
        Assert.Equal(ManagerId, creator.Created[0].AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Unassigned_When_Employee_Has_No_Manager()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator, managerId: null);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Null(creator.Created[0].AssignedEmployeeId);
        Assert.Null(creator.Created[0].AssignedUserId);
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
    public async Task HandleAsync_ActionType_Is_Review()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskActionType.Review, creator.Created[0].ActionType);
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
    public async Task HandleAsync_SourceEntityId_Is_ReviewId()
    {
        var creator  = new FakeTaskCreator();
        var handler  = BuildHandler(creator);
        var reviewId = Guid.NewGuid();

        await handler.HandleAsync(MakeEvent(reviewId: reviewId), CancellationToken.None);

        Assert.Equal(reviewId, creator.Created[0].SourceEntityId);
    }

    [Fact]
    public async Task HandleAsync_Description_Mentions_Return_To_Work()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator, employeeName: "Alice Smith");

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Contains("return-to-work", creator.Created[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_CreatedBy_Is_SystemUser()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(Guid.Empty, creator.Created[0].CreatedBy);
    }
}

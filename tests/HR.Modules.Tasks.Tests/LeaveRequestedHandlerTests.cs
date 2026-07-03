using HR.Modules.Tasks.Features.LeaveRequested;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests;

public class LeaveRequestedHandlerTests
{
    private static readonly Guid CompanyId  = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ManagerId  = Guid.NewGuid();

    private static LeaveRequestedIntegrationEvent MakeEvent(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        decimal totalDays = 3m) =>
        new(
            CompanyId,
            EmployeeId,
            LeaveRequestId: Guid.NewGuid(),
            LeaveTypeId:    Guid.NewGuid(),
            StartDate:      startDate ?? new DateOnly(2099, 8, 1),
            EndDate:        endDate   ?? new DateOnly(2099, 8, 5),
            TotalDays:      totalDays,
            OccurredAt:     DateTimeOffset.UtcNow);

    private static LeaveRequestedHandler BuildHandler(
        FakeTaskCreator taskCreator,
        Guid? managerId = null,
        string? employeeName = null)
    {
        var names = employeeName is not null
            ? new Dictionary<Guid, string> { [EmployeeId] = employeeName }
            : null;

        return new LeaveRequestedHandler(
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

        Assert.Equal("Review leave request — Alice Smith", creator.Created[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Title_Falls_Back_When_Name_Unknown()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator, employeeName: null);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal("Review leave request — Unknown Employee", creator.Created[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Assigns_To_Manager_When_Employee_Has_One()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator, managerId: ManagerId);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(ManagerId, creator.Created[0].AssignedEmployeeId);
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
    public async Task HandleAsync_Due_Date_Is_Leave_Start_Date_When_In_Future()
    {
        var creator   = new FakeTaskCreator();
        var handler   = BuildHandler(creator);
        var startDate = new DateOnly(2099, 9, 1);

        await handler.HandleAsync(MakeEvent(startDate: startDate), CancellationToken.None);

        Assert.Equal(startDate, creator.Created[0].DueDate);
    }

    [Fact]
    public async Task HandleAsync_Due_Date_Is_Today_When_Leave_Has_Already_Started()
    {
        var creator   = new FakeTaskCreator();
        var handler   = BuildHandler(creator);
        var startDate = new DateOnly(2000, 1, 1);

        await handler.HandleAsync(MakeEvent(startDate: startDate, endDate: new DateOnly(2000, 1, 5)), CancellationToken.None);

        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), creator.Created[0].DueDate);
    }

    [Fact]
    public async Task HandleAsync_Source_Is_Leave()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(TaskSource.Leave, creator.Created[0].Source);
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
    public async Task HandleAsync_Description_Contains_Date_Range_And_Day_Count()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);
        var evt = MakeEvent(
            startDate: new DateOnly(2099, 8, 1),
            endDate:   new DateOnly(2099, 8, 5),
            totalDays: 3m);

        await handler.HandleAsync(evt, CancellationToken.None);

        var description = creator.Created[0].Description;
        Assert.Contains("1 Aug 2099", description);
        Assert.Contains("5 Aug 2099", description);
        Assert.Contains("3 days", description);
    }

    [Fact]
    public async Task HandleAsync_Description_Uses_Singular_Day_When_One_Day()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(totalDays: 1m), CancellationToken.None);

        Assert.Contains("1 day", creator.Created[0].Description);
        Assert.DoesNotContain("1 days", creator.Created[0].Description);
    }

    [Fact]
    public async Task HandleAsync_CompanyId_Matches_Event()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(CompanyId, creator.Created[0].CompanyId);
    }

    [Fact]
    public async Task HandleAsync_CreatedBy_Is_Employee()
    {
        var creator = new FakeTaskCreator();
        var handler = BuildHandler(creator);

        await handler.HandleAsync(MakeEvent(), CancellationToken.None);

        Assert.Equal(EmployeeId, creator.Created[0].CreatedBy);
    }

    [Fact]
    public async Task HandleAsync_SourceEntityId_Is_LeaveRequestId()
    {
        var creator        = new FakeTaskCreator();
        var handler        = BuildHandler(creator);
        var leaveRequestId = Guid.NewGuid();
        var evt = new LeaveRequestedIntegrationEvent(
            CompanyId, EmployeeId,
            LeaveRequestId: leaveRequestId,
            LeaveTypeId:    Guid.NewGuid(),
            StartDate:      new DateOnly(2099, 8, 1),
            EndDate:        new DateOnly(2099, 8, 5),
            TotalDays:      3m,
            OccurredAt:     DateTimeOffset.UtcNow);

        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Equal(leaveRequestId, creator.Created[0].SourceEntityId);
    }
}

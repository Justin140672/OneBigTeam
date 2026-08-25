using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetWorkloadActions;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetWorkloadActionsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private static WorkloadAction Action(
        Guid? employeeId = null,
        string employeeName = "Employee",
        string? department = "Engineering",
        string actionType = "Approve Leave Request",
        string actionCategory = "Pending Leave Approvals",
        DateOnly? dueDate = null,
        string? assignedTo = null,
        string status = "Pending",
        string deepLinkUrl = "/companies/x/employees/x/view") =>
        new(
            employeeId ?? Guid.NewGuid(), employeeName, department, actionType, actionCategory,
            dueDate, assignedTo, status, deepLinkUrl);

    private static ClaimsPrincipal AnyCaller() => new(new ClaimsIdentity());

    // Manager/Location/RecruitmentUser filters are not exercised by these existing tests (see
    // GetWorkloadActionsHandlerTests additions for that coverage) — these fakes return empty/no-op
    // results so the handler's default (no manager/location/recruiter filter applied) behaviour is
    // unaffected.
    private static GetWorkloadActionsHandler MakeHandler(
        IEnumerable<IWorkloadActionProvider> providers, HR.SharedKernel.IClock clock) =>
        new(
            new FakeServiceScopeFactory([.. providers]),
            new FakeEmployeeDirectoryReader([]),
            new FakeEmployeeRecruiterReader(),
            clock);

    [Fact]
    public async Task HandleAsync_Merges_Results_From_Multiple_Providers()
    {
        var providerA = new FakeWorkloadActionProvider("Category A", Action(actionCategory: "Category A"));
        var providerB = new FakeWorkloadActionProvider("Category B", Action(actionCategory: "Category B"), Action(actionCategory: "Category B"));
        var handler = MakeHandler([providerA, providerB], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new GetWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
    }

    [Theory]
    [InlineData(-1, WorkloadActionUrgency.Overdue)]
    [InlineData(0, WorkloadActionUrgency.DueToday)]
    [InlineData(7, WorkloadActionUrgency.DueThisWeek)]
    [InlineData(8, WorkloadActionUrgency.Upcoming)]
    public async Task HandleAsync_Computes_Urgency_Centrally_Against_Clock(int dueDateOffsetDays, WorkloadActionUrgency expected)
    {
        var provider = new FakeWorkloadActionProvider("Cat", Action(dueDate: Today.AddDays(dueDateOffsetDays)));
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new GetWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(expected.ToString(), item.Urgency);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_ActionType()
    {
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(actionType: "Approve Leave Request", actionCategory: "Pending Leave Approvals"),
            Action(actionType: "Complete Return to Work Review", actionCategory: "Pending Sickness Actions"));
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), ActionType: "Leave"), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Approve Leave Request", item.ActionType);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Department()
    {
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(department: "Engineering"),
            Action(department: "Sales"));
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), Department: "Sales"), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Sales", item.Department);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status()
    {
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(status: "Pending"),
            Action(status: "Overdue"));
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), Status: "Overdue"), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Overdue", item.Status);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_EmployeeId()
    {
        var employeeId = Guid.NewGuid();
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(employeeId: employeeId),
            Action(employeeId: Guid.NewGuid()));
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), EmployeeId: employeeId), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeId, item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_DueDateStart_And_DueDateEnd()
    {
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(dueDate: Today.AddDays(-10)),
            Action(dueDate: Today.AddDays(2)),
            Action(dueDate: Today.AddDays(20)));
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), DueDateStart: Today.AddDays(-1), DueDateEnd: Today.AddDays(10)),
            AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(Today.AddDays(2), item.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Urgency()
    {
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(dueDate: Today.AddDays(-1)), // Overdue
            Action(dueDate: Today.AddDays(20))); // Upcoming
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), Urgency: "Overdue"), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Overdue", item.Urgency);
    }

    [Theory]
    [InlineData("ActionType")]
    [InlineData("AssignedUser")]
    [InlineData("Department")]
    [InlineData("DueDate")]
    public async Task HandleAsync_Groups_By_Requested_Key(string groupBy)
    {
        var provider = new FakeWorkloadActionProvider("Cat", Action(), Action());
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), GroupBy: groupBy), AnyCaller(), CancellationToken.None);

        Assert.NotEmpty(result.Value!.Groups);
    }

    [Fact]
    public async Task HandleAsync_GroupBy_Null_Or_Unrecognised_Returns_No_Groups()
    {
        var provider = new FakeWorkloadActionProvider("Cat", Action());
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var resultNull = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), GroupBy: null), AnyCaller(), CancellationToken.None);
        var resultUnrecognised = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), GroupBy: "NotARealKey"), AnyCaller(), CancellationToken.None);

        Assert.Empty(resultNull.Value!.Groups);
        Assert.Empty(resultUnrecognised.Value!.Groups);
    }

    [Fact]
    public async Task HandleAsync_Computes_Summary_Card_Counts()
    {
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(dueDate: Today.AddDays(-1)),  // Overdue
            Action(dueDate: Today.AddDays(-2)),  // Overdue
            Action(dueDate: Today),              // DueToday
            Action(dueDate: Today.AddDays(3)),   // DueThisWeek
            Action(dueDate: Today.AddDays(30))); // Upcoming
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new GetWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        var summary = result.Value!.Summary;
        Assert.Equal(5, summary.TotalOutstanding);
        Assert.Equal(2, summary.Overdue);
        Assert.Equal(1, summary.DueToday);
        Assert.Equal(1, summary.DueThisWeek);
    }

    // ── Manager/Location/RecruitmentUser filters (OBT-721 extension) ───────────

    private static EmployeeDirectoryReportItem DirectoryItem(Guid employeeId) =>
        new(employeeId, "EMP-001", "Employee", "Engineering", "Engineer", "Manager",
            "Full-Time", new DateOnly(2026, 1, 1), "Active", "London", "employee@example.com");

    [Fact]
    public async Task HandleAsync_Filters_By_ManagerId_Narrows_To_Matching_Employees()
    {
        var matchingEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(employeeId: matchingEmployeeId),
            Action(employeeId: otherEmployeeId));

        var directoryReader = new FakeEmployeeDirectoryReader([DirectoryItem(matchingEmployeeId)]);
        var handler = new GetWorkloadActionsHandler(
            new FakeServiceScopeFactory([provider]), directoryReader, new FakeEmployeeRecruiterReader(), new FakeClock(FixedUtcNow));

        var managerId = Guid.NewGuid();
        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), ManagerId: managerId), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(matchingEmployeeId, item.EmployeeId);
        Assert.Equal(managerId, directoryReader.LastFilter!.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_LocationId_Narrows_To_Matching_Employees()
    {
        var matchingEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(employeeId: matchingEmployeeId),
            Action(employeeId: otherEmployeeId));

        var directoryReader = new FakeEmployeeDirectoryReader([DirectoryItem(matchingEmployeeId)]);
        var handler = new GetWorkloadActionsHandler(
            new FakeServiceScopeFactory([provider]), directoryReader, new FakeEmployeeRecruiterReader(), new FakeClock(FixedUtcNow));

        var locationId = Guid.NewGuid();
        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), LocationId: locationId), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(matchingEmployeeId, item.EmployeeId);
        Assert.Equal(locationId, directoryReader.LastFilter!.LocationId);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_RecruitmentUser_Narrows_By_Recruiter_Name_Substring()
    {
        var matchingEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(employeeId: matchingEmployeeId),
            Action(employeeId: otherEmployeeId));

        var recruiterReader = new FakeEmployeeRecruiterReader
        {
            RecruiterNames = new Dictionary<Guid, string>
            {
                [matchingEmployeeId] = "Jamie Recruiter",
                [otherEmployeeId] = "Someone Else",
            },
        };
        var handler = new GetWorkloadActionsHandler(
            new FakeServiceScopeFactory([provider]), new FakeEmployeeDirectoryReader([]), recruiterReader, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), RecruitmentUser: "jamie"), AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(matchingEmployeeId, item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Combines_ManagerId_And_RecruitmentUser_Filters()
    {
        var employeeA = Guid.NewGuid(); // matches manager + recruiter
        var employeeB = Guid.NewGuid(); // matches manager only
        var employeeC = Guid.NewGuid(); // matches neither
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(employeeId: employeeA), Action(employeeId: employeeB), Action(employeeId: employeeC));

        var directoryReader = new FakeEmployeeDirectoryReader([DirectoryItem(employeeA), DirectoryItem(employeeB)]);
        var recruiterReader = new FakeEmployeeRecruiterReader
        {
            RecruiterNames = new Dictionary<Guid, string>
            {
                [employeeA] = "Jamie Recruiter",
                [employeeB] = "Someone Else",
            },
        };
        var handler = new GetWorkloadActionsHandler(
            new FakeServiceScopeFactory([provider]), directoryReader, recruiterReader, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new GetWorkloadActionsRequest(Guid.NewGuid(), ManagerId: Guid.NewGuid(), RecruitmentUser: "jamie"),
            AnyCaller(), CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeA, item.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Sorts_Overdue_First()
    {
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(employeeName: "Upcoming Item", dueDate: Today.AddDays(30)),
            Action(employeeName: "Overdue Item", dueDate: Today.AddDays(-5)),
            Action(employeeName: "Due Today Item", dueDate: Today));
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new GetWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        var items = result.Value!.Items;
        Assert.Equal("Overdue Item", items[0].EmployeeName);
    }

    // ── REP-05: bounded results ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Below_DisplayRowLimit_Is_Not_Truncated()
    {
        var actions = Enumerable.Range(0, 5).Select(_ => Action()).ToArray();
        var provider = new FakeWorkloadActionProvider("Cat", actions);
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new GetWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(5, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Above_DisplayRowLimit_Is_Truncated_But_Reports_Full_Total()
    {
        const int overLimitBy = 500;
        var totalActions = ReportLimits.DisplayRowLimit + overLimitBy;
        var actions = Enumerable.Range(0, totalActions).Select(_ => Action()).ToArray();
        var provider = new FakeWorkloadActionProvider("Cat", actions);
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new GetWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(totalActions, result.Value.TotalCount);
        Assert.Equal(ReportLimits.DisplayRowLimit, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Summary_Counts_Reflect_Full_Filtered_Set_Not_Capped_Rows()
    {
        // Seed limit+500 overdue actions — if the summary were computed AFTER the display cap was
        // applied (a regression), Overdue/TotalOutstanding would read DisplayRowLimit instead of
        // the true total.
        const int overLimitBy = 500;
        var totalActions = ReportLimits.DisplayRowLimit + overLimitBy;
        var actions = Enumerable.Range(0, totalActions)
            .Select(_ => Action(dueDate: Today.AddDays(-1))) // Overdue
            .ToArray();
        var provider = new FakeWorkloadActionProvider("Cat", actions);
        var handler = MakeHandler([provider], new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new GetWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        var summary = result.Value!.Summary;
        Assert.True(result.Value.IsTruncated);
        Assert.Equal(totalActions, summary.TotalOutstanding);
        Assert.Equal(totalActions, summary.Overdue);
        Assert.Equal(ReportLimits.DisplayRowLimit, result.Value.Items.Count);
    }
}

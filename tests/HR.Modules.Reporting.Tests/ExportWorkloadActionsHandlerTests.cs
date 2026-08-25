using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportWorkloadActions;
using HR.Modules.Reporting.Features.GetWorkloadActions;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportWorkloadActionsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 29, 9, 0, 0, DateTimeKind.Utc);

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

    private static ExportWorkloadActionsHandler BuildHandler(
        IEnumerable<IWorkloadActionProvider> providers, FakeReportExporter exporter) =>
        new(
            new GetWorkloadActionsHandler(
                new FakeServiceScopeFactory([.. providers]),
                new FakeEmployeeDirectoryReader([]),
                new FakeEmployeeRecruiterReader(),
                new FakeClock(FixedUtcNow)),
            exporter,
            new FakeAuthorizationService(),
            TestReportExportAuditor.Create());

    private static System.Security.Claims.ClaimsPrincipal AnyCaller() =>
        new(new System.Security.Claims.ClaimsIdentity());

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetWorkloadActionsHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var provider = new FakeWorkloadActionProvider("Cat",
            Action(employeeId: employeeId, employeeName: "Jordan Employee", dueDate: new DateOnly(2026, 8, 1)));
        var exporter = new FakeReportExporter();
        var handler = BuildHandler([provider], exporter);

        var result = await handler.HandleAsync(
            new ExportWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Workload & HR Actions Report", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Employee", "Department", "Action Type", "Category", "Due Date", "Assigned To", "Status", "Urgency"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Jordan Employee", row[0]);
        Assert.Equal("Engineering", row[1]);
        Assert.Equal("Approve Leave Request", row[2]);
        Assert.Equal("Pending Leave Approvals", row[3]);
        Assert.Equal("2026-08-01", row[4]);
    }

    [Fact]
    public async Task HandleAsync_Exports_Empty_Rows_When_No_Actions()
    {
        var provider = new FakeWorkloadActionProvider("Cat");
        var exporter = new FakeReportExporter();
        var handler = BuildHandler([provider], exporter);

        var result = await handler.HandleAsync(
            new ExportWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(exporter.LastData!.Rows);
    }

    [Fact]
    public async Task HandleAsync_Forwards_Requested_Format_To_Exporter()
    {
        var provider = new FakeWorkloadActionProvider("Cat", Action());
        var exporter = new FakeReportExporter();
        var handler = BuildHandler([provider], exporter);

        var result = await handler.HandleAsync(
            new ExportWorkloadActionsRequest(Guid.NewGuid(), Format: ReportExportFormat.Pdf),
            AnyCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReportExportFormat.Pdf, exporter.LastFormat);
    }

    [Fact]
    public async Task HandleAsync_Forwards_Filters_Through_To_GetWorkloadActionsHandler()
    {
        var matching = Action(actionType: "Approve Leave Request", actionCategory: "Pending Leave Approvals");
        var nonMatching = Action(actionType: "Complete Return to Work Review", actionCategory: "Pending Sickness Actions");
        var provider = new FakeWorkloadActionProvider("Cat", matching, nonMatching);
        var exporter = new FakeReportExporter();
        var handler = BuildHandler([provider], exporter);

        var result = await handler.HandleAsync(
            new ExportWorkloadActionsRequest(Guid.NewGuid(), ActionType: "Leave"), AnyCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(exporter.LastData!.Rows);
        Assert.Equal("Approve Leave Request", row[2]);
    }

    [Fact]
    public async Task HandleAsync_Propagates_IsTruncated_And_TotalCount_From_GetHandler()
    {
        const int overLimitBy = 500;
        var totalActions = ReportLimits.DisplayRowLimit + overLimitBy;
        var actions = Enumerable.Range(0, totalActions).Select(_ => Action()).ToArray();
        var provider = new FakeWorkloadActionProvider("Cat", actions);
        var exporter = new FakeReportExporter();
        var handler = BuildHandler([provider], exporter);

        var result = await handler.HandleAsync(
            new ExportWorkloadActionsRequest(Guid.NewGuid()), AnyCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(totalActions, result.Value.TotalCount);
    }
}

using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportOnboardingProgressReport;
using HR.Modules.Reporting.Features.GetOnboardingProgressReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportOnboardingProgressReportHandlerTests
{
    private static OnboardingReportItem BuildItem(Guid employeeId) =>
        new(
            employeeId,
            Guid.NewGuid(),
            "InProgress",
            new DateOnly(2026, 1, 1),
            TotalTasks: 4,
            CompletedTasks: 2,
            OutstandingTasks: [new OnboardingReportTaskItem("Set up equipment", new DateOnly(2026, 2, 1), "IT", IsOverdue: false)]);

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader([BuildItem(employeeId)]);
        var getHandler = new GetOnboardingProgressReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportOnboardingProgressReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportOnboardingProgressReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Onboarding Progress Report", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Employee", "Plan Status", "Progress %", "Outstanding Tasks", "Has Overdue"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("InProgress", row[1]);
        Assert.Equal("50", row[2]);
    }

    [Fact]
    public async Task HandleAsync_ManagerWithNoDirectReports_Exports_Empty_Rows()
    {
        var reader = new FakeOnboardingReportReader([BuildItem(Guid.NewGuid())]);
        var directReportsReader = new FakeDirectReportsReader([]);
        var getHandler = new GetOnboardingProgressReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);
        var exporter = new FakeReportExporter();
        var handler = new ExportOnboardingProgressReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportOnboardingProgressReportRequest(Guid.NewGuid()),
            callerIsHr: false,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(exporter.LastData!.Rows);
    }
}

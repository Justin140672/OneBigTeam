using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportOffboardingProgressReport;
using HR.Modules.Reporting.Features.GetOffboardingProgressReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportOffboardingProgressReportHandlerTests
{
    private static OffboardingReportItem BuildItem(Guid employeeId) =>
        new(
            employeeId,
            new DateOnly(2026, 8, 1),
            "InProgress",
            TotalTasks: 3,
            CompletedTasks: 1,
            OutstandingTaskTitles: ["Return laptop"],
            CompletedTaskTitles: ["Exit interview"],
            DocumentsReturned: true);

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader([BuildItem(employeeId)]);
        var getHandler = new GetOffboardingProgressReportHandler(
            reader, new FakeEmployeeDepartmentReader(), new FakeEmployeeUserAccountStatusReader(), new FakeAssignedAssetReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportOffboardingProgressReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(new ExportOffboardingProgressReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Offboarding Progress Report", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Employee", "Last Working Day", "Status", "Outstanding Tasks", "Access Disabled", "Documents Returned", "Assets Returned"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("InProgress", row[2]);
        Assert.Equal("2026-08-01", row[1]);
    }

    [Fact]
    public async Task HandleAsync_Exports_Empty_Rows_When_No_Offboarding_Items()
    {
        var reader = new FakeOffboardingReportReader([]);
        var getHandler = new GetOffboardingProgressReportHandler(
            reader, new FakeEmployeeDepartmentReader(), new FakeEmployeeUserAccountStatusReader(), new FakeAssignedAssetReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportOffboardingProgressReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(new ExportOffboardingProgressReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(exporter.LastData!.Rows);
    }
}

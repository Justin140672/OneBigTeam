using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportProbationReport;
using HR.Modules.Reporting.Features.GetProbationReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportProbationReportHandlerTests
{
    private static ProbationReportItem BuildItem(Guid employeeId) =>
        new(
            employeeId,
            Guid.NewGuid(),
            "Active",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 4, 1),
            DueReviewCount: 1,
            OverdueReviewCount: 2);

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeProbationReportReader([BuildItem(employeeId)]);
        var getHandler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportProbationReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(
            new ExportProbationReportRequest(Guid.NewGuid()),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Probation Report", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Employee", "Status", "Start Date", "Expected End Date", "Due Reviews", "Overdue Reviews"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Active", row[1]);
        Assert.Equal("2026-01-01", row[2]);
    }

    [Fact]
    public async Task HandleAsync_ManagerWithNoDirectReports_Exports_Empty_Rows()
    {
        var reader = new FakeProbationReportReader([BuildItem(Guid.NewGuid())]);
        var directReportsReader = new FakeDirectReportsReader([]);
        var getHandler = new GetProbationReportHandler(reader, new FakeEmployeeDepartmentReader(), directReportsReader);
        var exporter = new FakeReportExporter();
        var handler = new ExportProbationReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(
            new ExportProbationReportRequest(Guid.NewGuid()),
            callerIsHr: false,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(exporter.LastData!.Rows);
    }
}

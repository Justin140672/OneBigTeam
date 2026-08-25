using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportLeaveSummaryReport;
using HR.Modules.Reporting.Features.GetLeaveSummaryReport;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportLeaveSummaryReportHandlerTests
{
    private static ExportLeaveSummaryReportHandler BuildHandler(
        FakeLeaveSummaryReader reader, FakeReportExporter exporter) =>
        new(
            new GetLeaveSummaryReportHandler(reader, new FakeEmployeeDepartmentReader(), new FakeDirectReportsReader()),
            exporter,
            TestReportExportAuditor.Create());

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var annualTypeId = Guid.NewGuid();
        var reader = new FakeLeaveSummaryReader(
        [
            new LeaveSummaryReportRow(employeeId, annualTypeId, "Annual Leave", 25m, 5m, 5m, 20m, 1),
        ]);
        var exporter = new FakeReportExporter();
        var handler = BuildHandler(reader, exporter);

        var result = await handler.HandleAsync(
            new ExportLeaveSummaryReportRequest(Guid.NewGuid(), null, null),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Leave Summary Report", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Group", "Entitlement Days", "Booked Days", "Approved Days", "Remaining Days", "Pending Requests"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("25", row[1]);
        Assert.Equal("1", row[5]);
    }

    [Fact]
    public async Task HandleAsync_Forwards_Requested_Format_To_Exporter()
    {
        var reader = new FakeLeaveSummaryReader([]);
        var exporter = new FakeReportExporter();
        var handler = BuildHandler(reader, exporter);

        await handler.HandleAsync(
            new ExportLeaveSummaryReportRequest(Guid.NewGuid(), null, null, Format: ReportExportFormat.Pdf),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(ReportExportFormat.Pdf, exporter.LastFormat);
    }

    [Fact]
    public async Task HandleAsync_Propagates_IsTruncated_And_TotalCount_From_GetHandler()
    {
        const int overLimitBy = 500;
        var totalGroups = ReportLimits.DisplayRowLimit + overLimitBy;
        var annualTypeId = Guid.NewGuid();
        var rows = Enumerable.Range(0, totalGroups)
            .Select(_ => new LeaveSummaryReportRow(Guid.NewGuid(), annualTypeId, "Annual Leave", 25m, 0m, 0m, 25m, 0))
            .ToList();
        var reader = new FakeLeaveSummaryReader(rows);
        var exporter = new FakeReportExporter();
        var handler = BuildHandler(reader, exporter);

        var result = await handler.HandleAsync(
            new ExportLeaveSummaryReportRequest(Guid.NewGuid(), null, null),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(totalGroups, result.Value.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Below_Limit_Reports_Not_Truncated()
    {
        var annualTypeId = Guid.NewGuid();
        var reader = new FakeLeaveSummaryReader(
        [
            new LeaveSummaryReportRow(Guid.NewGuid(), annualTypeId, "Annual Leave", 25m, 0m, 0m, 25m, 0),
        ]);
        var exporter = new FakeReportExporter();
        var handler = BuildHandler(reader, exporter);

        var result = await handler.HandleAsync(
            new ExportLeaveSummaryReportRequest(Guid.NewGuid(), null, null),
            callerIsHr: true,
            callerEmployeeId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(1, result.Value.TotalCount);
    }
}

using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportLeaveCalendarReport;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportLeaveCalendarReportHandlerTests
{
    private static LeaveCalendarReportItem BuildItem(Guid employeeId) =>
        new(employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5), "Annual Leave", 5m, "Approved");

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_Reader()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeLeaveCalendarReader([BuildItem(employeeId)]);
        var exporter = new FakeReportExporter();
        var handler = new ExportLeaveCalendarReportHandler(reader, new FakeEmployeeDepartmentReader(), exporter);

        var result = await handler.HandleAsync(new ExportLeaveCalendarReportRequest(Guid.NewGuid(), 2026, 8), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Leave Calendar Export", exporter.LastData!.ReportTitle);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Annual Leave", row[4]);
    }

    [Fact]
    public async Task HandleAsync_Below_ExportRowLimit_Is_Not_Truncated()
    {
        var items = Enumerable.Range(0, 5).Select(_ => BuildItem(Guid.NewGuid())).ToList();
        var reader = new FakeLeaveCalendarReader(items);
        var exporter = new FakeReportExporter();
        var handler = new ExportLeaveCalendarReportHandler(reader, new FakeEmployeeDepartmentReader(), exporter);

        var result = await handler.HandleAsync(new ExportLeaveCalendarReportRequest(Guid.NewGuid(), 2026, 8), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(5, result.Value.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Above_ExportRowLimit_Is_Truncated_But_Reports_Full_Total()
    {
        // Export handler caps at ExportRowLimit (50,000), distinct from the Get handler's
        // DisplayRowLimit (20,000) — this seeds well above DisplayRowLimit but still below
        // ExportRowLimit to prove the export cap, not the display cap, governs here, then a
        // second case above ExportRowLimit to prove truncation kicks in at the right threshold.
        const int overLimitBy = 500;
        var totalItems = ReportLimits.ExportRowLimit + overLimitBy;
        var items = Enumerable.Range(0, totalItems).Select(_ => BuildItem(Guid.NewGuid())).ToList();
        var reader = new FakeLeaveCalendarReader(items);
        var exporter = new FakeReportExporter();
        var handler = new ExportLeaveCalendarReportHandler(reader, new FakeEmployeeDepartmentReader(), exporter);

        var result = await handler.HandleAsync(new ExportLeaveCalendarReportRequest(Guid.NewGuid(), 2026, 8), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(totalItems, result.Value.TotalCount);
        Assert.Equal(ReportLimits.ExportRowLimit, exporter.LastData!.Rows.Count);
    }

    [Fact]
    public async Task HandleAsync_At_Or_Below_DisplayRowLimit_But_Not_ExportRowLimit_Is_Not_Truncated()
    {
        // Proves the export path uses ExportRowLimit (50,000), not DisplayRowLimit (20,000): a
        // volume that would truncate the Get*/display endpoint must NOT truncate the export.
        const int overDisplayLimitBy = 500;
        var totalItems = ReportLimits.DisplayRowLimit + overDisplayLimitBy;
        var items = Enumerable.Range(0, totalItems).Select(_ => BuildItem(Guid.NewGuid())).ToList();
        var reader = new FakeLeaveCalendarReader(items);
        var exporter = new FakeReportExporter();
        var handler = new ExportLeaveCalendarReportHandler(reader, new FakeEmployeeDepartmentReader(), exporter);

        var result = await handler.HandleAsync(new ExportLeaveCalendarReportRequest(Guid.NewGuid(), 2026, 8), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(totalItems, result.Value.TotalCount);
    }
}

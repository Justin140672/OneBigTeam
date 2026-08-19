using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportHrHeadcountSummaryReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportHrHeadcountSummaryReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Exports_Rows_From_Reader_Result()
    {
        var employeeId = Guid.NewGuid();
        var items = new List<HrHeadcountSummaryItem>
        {
            new(
                employeeId,
                "Alice Smith",
                "Engineering",
                "London",
                "Senior Developer",
                "Full Time",
                "Active",
                new DateOnly(2026, 1, 1),
                null,
                1.0m),
        };
        var reader = new FakeHrHeadcountSummaryReader(
            new HrHeadcountSummaryResult(items, 1, 1, 0, 0, 1.0m));
        var exporter = new FakeReportExporter();
        var handler = new ExportHrHeadcountSummaryReportHandler(reader, exporter);

        var result = await handler.HandleAsync(
            new ExportHrHeadcountSummaryReportRequest(Guid.NewGuid(), null, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("HR Headcount Summary", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Employee", "Department", "Location", "Position", "Employment Type", "Employee Status", "Start Date", "Leaving Date", "FTE"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Alice Smith", row[0]);
        Assert.Equal("2026-01-01", row[6]);
        Assert.Null(row[7]);
        Assert.Equal("1.00", row[8]);
    }

    [Fact]
    public async Task HandleAsync_Passes_Filters_Through_To_Reader()
    {
        var reader = new FakeHrHeadcountSummaryReader();
        var exporter = new FakeReportExporter();
        var handler = new ExportHrHeadcountSummaryReportHandler(reader, exporter);
        var departmentId = Guid.NewGuid();

        await handler.HandleAsync(
            new ExportHrHeadcountSummaryReportRequest(Guid.NewGuid(), departmentId, null, null, "Leaver"), CancellationToken.None);

        Assert.Equal(departmentId, reader.LastFilter!.DepartmentId);
        Assert.Equal("Leaver", reader.LastFilter.EmployeeStatus);
    }
}

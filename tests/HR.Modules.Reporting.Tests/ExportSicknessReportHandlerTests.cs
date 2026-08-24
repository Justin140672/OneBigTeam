using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportSicknessReport;
using HR.Modules.Reporting.Features.GetSicknessReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportSicknessReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeSicknessReportReader(
        [
            new SicknessReportRecordItem(employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 2m),
        ]);
        var getHandler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportSicknessReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(
            new ExportSicknessReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(exporter.LastData);
        Assert.Equal("Sickness Report", exporter.LastData!.ReportTitle);
        Assert.Equal(["Group", "Absence Count", "Days Absent", "Bradford Score"], exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("1", row[1]);
        Assert.Equal("2", row[2]);
        // SICK-04: Bradford Factor = S^2 * D = 1^2 * 2 = 2.
        Assert.Equal("2", row[3]);
    }

    [Fact]
    public async Task HandleAsync_Passes_Format_To_Exporter()
    {
        var reader = new FakeSicknessReportReader([]);
        var getHandler = new GetSicknessReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportSicknessReportHandler(getHandler, exporter);

        await handler.HandleAsync(
            new ExportSicknessReportRequest(Guid.NewGuid(), Format: ReportExportFormat.Pdf), CancellationToken.None);

        Assert.Equal(ReportExportFormat.Pdf, exporter.LastFormat);
    }
}

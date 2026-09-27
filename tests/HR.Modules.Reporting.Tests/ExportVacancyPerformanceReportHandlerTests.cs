using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportVacancyPerformanceReport;
using HR.Modules.Reporting.Features.GetVacancyPerformanceReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportVacancyPerformanceReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var reader = new FakeVacancyPerformanceReader(
        [
            new VacancyPerformanceItem(Guid.NewGuid(), "Engineer", new DateOnly(2026, 1, 1), null, 40, 12, 5, 2, new DateOnly(2026, 3, 1)),
        ]);
        var getHandler = new GetVacancyPerformanceReportHandler(reader);
        var exporter = new FakeReportExporter();
        var handler = new ExportVacancyPerformanceReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportVacancyPerformanceReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Vacancy Performance Report", exporter.LastData!.ReportTitle);
        Assert.Equal(["Vacancy", "Days Open", "Candidates", "Interviews", "Offers", "Hire Date"], exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Engineer", row[0]);
        Assert.Equal("2026-03-01", row[5]);
    }

    [Fact]
    public async Task HandleAsync_Exports_Null_HireDate_As_Null()
    {
        var reader = new FakeVacancyPerformanceReader(
        [
            new VacancyPerformanceItem(Guid.NewGuid(), "Engineer", new DateOnly(2026, 1, 1), null, 40, 12, 5, 2, null),
        ]);
        var getHandler = new GetVacancyPerformanceReportHandler(reader);
        var exporter = new FakeReportExporter();
        var handler = new ExportVacancyPerformanceReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        await handler.HandleAsync(new ExportVacancyPerformanceReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(exporter.LastData!.Rows);
        Assert.Null(row[5]);
    }

    // ----- Internal recruitment Ticket 6: isInternal is passed through to the reader -----

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_Passes_IsInternal_Through_To_Reader(bool? isInternal)
    {
        var reader = new FakeVacancyPerformanceReader([]);
        var getHandler = new GetVacancyPerformanceReportHandler(reader);
        var exporter = new FakeReportExporter();
        var handler = new ExportVacancyPerformanceReportHandler(getHandler, exporter, TestReportExportAuditor.Create());
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 1, 31);

        var result = await handler.HandleAsync(
            new ExportVacancyPerformanceReportRequest(Guid.NewGuid(), start, end, IsInternal: isInternal), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(isInternal, reader.LastIsInternal);
        Assert.Equal(start, reader.LastStartDate);
        Assert.Equal(end, reader.LastEndDate);
    }
}

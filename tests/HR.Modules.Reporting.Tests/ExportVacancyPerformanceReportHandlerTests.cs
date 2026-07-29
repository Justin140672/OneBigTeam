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
        var handler = new ExportVacancyPerformanceReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(
            new ExportVacancyPerformanceReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Vacancy Performance Report", exporter.LastData!.ReportTitle);
        Assert.Equal(["Vacancy", "Days Open", "Applicants", "Interviews", "Offers", "Hire Date"], exporter.LastData.ColumnHeaders);
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
        var handler = new ExportVacancyPerformanceReportHandler(getHandler, exporter);

        await handler.HandleAsync(new ExportVacancyPerformanceReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(exporter.LastData!.Rows);
        Assert.Null(row[5]);
    }
}

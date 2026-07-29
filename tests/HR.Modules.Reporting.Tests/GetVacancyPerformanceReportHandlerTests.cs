using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetVacancyPerformanceReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetVacancyPerformanceReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Maps_Reader_Items_Into_Response_Rows()
    {
        var vacancyId = Guid.NewGuid();
        var hireDate = new DateOnly(2026, 3, 1);
        var reader = new FakeVacancyPerformanceReader(
        [
            new VacancyPerformanceItem(vacancyId, "Engineer", new DateOnly(2026, 1, 1), null, 40, 12, 5, 2, hireDate),
        ]);
        var handler = new GetVacancyPerformanceReportHandler(reader);

        var result = await handler.HandleAsync(
            new GetVacancyPerformanceReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(vacancyId, row.VacancyId);
        Assert.Equal("Engineer", row.VacancyTitle);
        Assert.Equal(40, row.DaysOpen);
        Assert.Equal(12, row.ApplicantCount);
        Assert.Equal(5, row.InterviewCount);
        Assert.Equal(2, row.OfferCount);
        Assert.Equal(hireDate, row.HireDate);
    }

    [Fact]
    public async Task HandleAsync_Passes_Date_Range_To_Reader()
    {
        var reader = new FakeVacancyPerformanceReader([]);
        var handler = new GetVacancyPerformanceReportHandler(reader);
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 1, 31);
        var companyId = Guid.NewGuid();

        await handler.HandleAsync(new GetVacancyPerformanceReportRequest(companyId, start, end), CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.Equal(start, reader.LastStartDate);
        Assert.Equal(end, reader.LastEndDate);
    }
}

using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportRecruitmentPipelineReport;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportRecruitmentPipelineReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var reader = new FakeRecruitmentPipelineReader(
            recruiterRows: [new RecruitmentPipelineRecruiterRow(Guid.NewGuid(), "Bob", 3, 10, 4, 2, 1)]);
        var getHandler = new GetRecruitmentPipelineReportHandler(reader);
        var exporter = new FakeReportExporter();
        var handler = new ExportRecruitmentPipelineReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportRecruitmentPipelineReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Recruitment Pipeline Report", exporter.LastData!.ReportTitle);
        Assert.Equal(["Group", "Vacancies", "Applicants", "Interviews", "Offers", "Hires"], exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Bob", row[0]);
        Assert.Equal("1", row[5]);
    }
}

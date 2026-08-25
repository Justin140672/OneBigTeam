using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportRecruitmentPipelineSummaryReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportRecruitmentPipelineSummaryReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Exports_Fixed_And_Stage_Columns()
    {
        var stageId = Guid.NewGuid();
        var stages = new List<RecruitmentStageColumn> { new(stageId, "Screening") };
        var rows = new List<RecruitmentPipelineSummaryRow>
        {
            new(
                Guid.NewGuid(),
                "Engineer",
                "Software Engineer",
                "Engineering",
                "Open",
                new DateOnly(2026, 1, 1),
                5,
                new Dictionary<Guid, int> { [stageId] = 3 }),
        };
        var reader = new FakeRecruitmentPipelineSummaryReader(
            new RecruitmentPipelineSummaryResult(rows, stages));
        var exporter = new FakeReportExporter();
        var handler = new ExportRecruitmentPipelineSummaryReportHandler(reader, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportRecruitmentPipelineSummaryReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Recruitment Pipeline Summary", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Vacancy", "Position Profile", "Department", "Status", "Date Opened", "Candidates", "Screening"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Engineer", row[0]);
        Assert.Equal("3", row[^1]);
    }

    [Fact]
    public async Task HandleAsync_Defaults_Missing_Stage_Count_To_Zero()
    {
        var stageId = Guid.NewGuid();
        var stages = new List<RecruitmentStageColumn> { new(stageId, "Offer") };
        var rows = new List<RecruitmentPipelineSummaryRow>
        {
            new(Guid.NewGuid(), "Engineer", null, null, "Open", null, 0, new Dictionary<Guid, int>()),
        };
        var reader = new FakeRecruitmentPipelineSummaryReader(
            new RecruitmentPipelineSummaryResult(rows, stages));
        var exporter = new FakeReportExporter();
        var handler = new ExportRecruitmentPipelineSummaryReportHandler(reader, exporter, TestReportExportAuditor.Create());

        await handler.HandleAsync(new ExportRecruitmentPipelineSummaryReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(exporter.LastData!.Rows);
        Assert.Equal("0", row[^1]);
    }

    [Fact]
    public async Task HandleAsync_Passes_IncludeClosed_Through_To_Reader()
    {
        var reader = new FakeRecruitmentPipelineSummaryReader();
        var exporter = new FakeReportExporter();
        var handler = new ExportRecruitmentPipelineSummaryReportHandler(reader, exporter, TestReportExportAuditor.Create());

        await handler.HandleAsync(
            new ExportRecruitmentPipelineSummaryReportRequest(Guid.NewGuid(), IncludeClosed: true), CancellationToken.None);

        Assert.True(reader.LastIncludeClosed);
    }
}

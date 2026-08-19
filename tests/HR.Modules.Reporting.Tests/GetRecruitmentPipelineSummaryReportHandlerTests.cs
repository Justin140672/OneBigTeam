using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetRecruitmentPipelineSummaryReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Passes_CompanyId_And_IncludeClosed_To_Reader()
    {
        var companyId = Guid.NewGuid();
        var reader = new FakeRecruitmentPipelineSummaryReader();
        var handler = new GetRecruitmentPipelineSummaryReportHandler(reader);

        await handler.HandleAsync(
            new GetRecruitmentPipelineSummaryReportRequest(companyId, IncludeClosed: true), CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.True(reader.LastIncludeClosed);
    }

    [Fact]
    public async Task HandleAsync_Defaults_IncludeClosed_To_False()
    {
        var reader = new FakeRecruitmentPipelineSummaryReader();
        var handler = new GetRecruitmentPipelineSummaryReportHandler(reader);

        await handler.HandleAsync(
            new GetRecruitmentPipelineSummaryReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.False(reader.LastIncludeClosed);
    }

    [Fact]
    public async Task HandleAsync_Maps_Reader_Result_Into_Response()
    {
        var vacancyId = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var stages = new List<RecruitmentStageColumn> { new(stageId, "Screening") };
        var rows = new List<RecruitmentPipelineSummaryRow>
        {
            new(
                vacancyId,
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
        var handler = new GetRecruitmentPipelineSummaryReportHandler(reader);

        var result = await handler.HandleAsync(
            new GetRecruitmentPipelineSummaryReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        var stage = Assert.Single(response.Stages);
        Assert.Equal("Screening", stage.StageName);
        var row = Assert.Single(response.Vacancies);
        Assert.Equal("Engineer", row.VacancyTitle);
        Assert.Equal(3, row.CandidatesByStage[stageId]);
    }
}

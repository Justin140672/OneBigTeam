using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetRecruitmentPipelineReportHandlerTests
{
    [Fact]
    public async Task HandleAsync_Uses_ByRecruiter_Reader_By_Default()
    {
        var recruiterId = Guid.NewGuid();
        var reader = new FakeRecruitmentPipelineReader(
            recruiterRows: [new RecruitmentPipelineRecruiterRow(recruiterId, "Bob", 3, 10, 4, 2, 1)]);
        var handler = new GetRecruitmentPipelineReportHandler(reader);

        var result = await handler.HandleAsync(
            new GetRecruitmentPipelineReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(reader.ByRecruiterCalled);
        Assert.False(reader.ByVacancyCalled);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(recruiterId.ToString(), row.GroupKey);
        Assert.Equal("Bob", row.GroupLabel);
        Assert.Equal(3, row.Vacancies);
        Assert.Equal(10, row.Candidates);
        Assert.Equal(1, row.Hires);
    }

    [Fact]
    public async Task HandleAsync_Uses_UnassignedGroupKey_When_Recruiter_Is_Null()
    {
        var reader = new FakeRecruitmentPipelineReader(
            recruiterRows: [new RecruitmentPipelineRecruiterRow(null, "Unassigned", 1, 2, 0, 0, 0)]);
        var handler = new GetRecruitmentPipelineReportHandler(reader);

        var result = await handler.HandleAsync(
            new GetRecruitmentPipelineReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("unassigned", row.GroupKey);
    }

    [Fact]
    public async Task HandleAsync_Uses_ByVacancy_Reader_When_GroupBy_Vacancy()
    {
        var vacancyId = Guid.NewGuid();
        var reader = new FakeRecruitmentPipelineReader(
            vacancyRows: [new RecruitmentPipelineVacancyRow(vacancyId, "Engineer", 5, 3, 1, 1)]);
        var handler = new GetRecruitmentPipelineReportHandler(reader);

        var result = await handler.HandleAsync(
            new GetRecruitmentPipelineReportRequest(Guid.NewGuid(), GroupBy: RecruitmentPipelineGroupBy.Vacancy),
            CancellationToken.None);

        Assert.True(reader.ByVacancyCalled);
        Assert.False(reader.ByRecruiterCalled);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(vacancyId.ToString(), row.GroupKey);
        Assert.Equal("Engineer", row.GroupLabel);
        Assert.Equal(1, row.Vacancies);
        Assert.Equal(5, row.Candidates);
    }

    // ----- Internal recruitment Ticket 6: isInternal is passed through to the reader -----

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_Passes_IsInternal_To_ByRecruiter_Reader(bool? isInternal)
    {
        var reader = new FakeRecruitmentPipelineReader();
        var handler = new GetRecruitmentPipelineReportHandler(reader);

        var result = await handler.HandleAsync(
            new GetRecruitmentPipelineReportRequest(Guid.NewGuid(), IsInternal: isInternal), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(reader.ByRecruiterCalled);
        Assert.Equal(isInternal, reader.LastIsInternal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_Passes_IsInternal_To_ByVacancy_Reader(bool? isInternal)
    {
        var reader = new FakeRecruitmentPipelineReader();
        var handler = new GetRecruitmentPipelineReportHandler(reader);
        var companyId = Guid.NewGuid();
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        var result = await handler.HandleAsync(
            new GetRecruitmentPipelineReportRequest(companyId, start, end, RecruitmentPipelineGroupBy.Vacancy, isInternal),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(reader.ByVacancyCalled);
        Assert.Equal(isInternal, reader.LastIsInternal);
        Assert.Equal(companyId, reader.LastCompanyId);
        Assert.Equal(start, reader.LastStartDate);
        Assert.Equal(end, reader.LastEndDate);
    }
}

using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IRecruitmentPipelineReader"/> — records the arguments it was
/// called with and returns pre-configured recruiter/vacancy rows.
/// </summary>
internal sealed class FakeRecruitmentPipelineReader : IRecruitmentPipelineReader
{
    private readonly IReadOnlyList<RecruitmentPipelineRecruiterRow> _recruiterRows;
    private readonly IReadOnlyList<RecruitmentPipelineVacancyRow> _vacancyRows;

    public FakeRecruitmentPipelineReader(
        IReadOnlyList<RecruitmentPipelineRecruiterRow>? recruiterRows = null,
        IReadOnlyList<RecruitmentPipelineVacancyRow>? vacancyRows = null)
    {
        _recruiterRows = recruiterRows ?? [];
        _vacancyRows = vacancyRows ?? [];
    }

    public Guid? LastCompanyId { get; private set; }
    public DateOnly? LastStartDate { get; private set; }
    public DateOnly? LastEndDate { get; private set; }
    public bool ByRecruiterCalled { get; private set; }
    public bool ByVacancyCalled { get; private set; }

    public Task<IReadOnlyList<RecruitmentPipelineRecruiterRow>> GetByRecruiterAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastStartDate = startDate;
        LastEndDate = endDate;
        ByRecruiterCalled = true;

        return Task.FromResult(_recruiterRows);
    }

    public Task<IReadOnlyList<RecruitmentPipelineVacancyRow>> GetByVacancyAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastStartDate = startDate;
        LastEndDate = endDate;
        ByVacancyCalled = true;

        return Task.FromResult(_vacancyRows);
    }
}

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide recruitment pipeline funnel metrics for the Recruitment Pipeline Report (OBT-709),
/// as owned by HR.Modules.Recruitment. "Offers" are counted as distinct applications that have ever
/// reached the company's "Offer" named RecruitmentStage (there is no separate Offer entity in the
/// domain — see ApplicationStageHistoryEntry/RecruitmentStageSeeder). Date range filtering is
/// applied against Application.AppliedAt.
/// </summary>
public interface IRecruitmentPipelineReader
{
    Task<IReadOnlyList<RecruitmentPipelineRecruiterRow>> GetByRecruiterAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecruitmentPipelineVacancyRow>> GetByVacancyAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken);
}

public sealed record RecruitmentPipelineRecruiterRow(
    Guid? RecruiterId,
    string RecruiterName,
    int Vacancies,
    int Applicants,
    int Interviews,
    int Offers,
    int Hires);

public sealed record RecruitmentPipelineVacancyRow(
    Guid VacancyId,
    string VacancyTitle,
    int Applicants,
    int Interviews,
    int Offers,
    int Hires);

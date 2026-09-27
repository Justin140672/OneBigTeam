namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide recruitment pipeline funnel metrics for the Recruitment Pipeline Report (OBT-709),
/// as owned by HR.Modules.Recruitment. "Offers" are counted as distinct applications that have ever
/// reached the company's "Offer" named RecruitmentStage (there is no separate Offer entity in the
/// domain — see ApplicationStageHistoryEntry/RecruitmentStageSeeder). Date range filtering is
/// applied against Application.AppliedAt.
/// Internal recruitment Ticket 6: <c>isInternal</c> optionally restricts the counted applications —
/// null = all, true = only internal applications (Application.Source == Internal), false = all
/// other applications. Internal status is never inferred from a candidate's employee link.
/// </summary>
public interface IRecruitmentPipelineReader
{
    Task<IReadOnlyList<RecruitmentPipelineRecruiterRow>> GetByRecruiterAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        bool? isInternal,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecruitmentPipelineVacancyRow>> GetByVacancyAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        bool? isInternal,
        CancellationToken cancellationToken);
}

public sealed record RecruitmentPipelineRecruiterRow(
    Guid? RecruiterId,
    string RecruiterName,
    int Vacancies,
    int Candidates,
    int Interviews,
    int Offers,
    int Hires);

public sealed record RecruitmentPipelineVacancyRow(
    Guid VacancyId,
    string VacancyTitle,
    int Candidates,
    int Interviews,
    int Offers,
    int Hires);

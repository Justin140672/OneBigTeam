namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Per-vacancy performance metrics for the Vacancy Performance Report (OBT-710), as owned by
/// HR.Modules.Recruitment. Shares underlying query logic with IRecruitmentPipelineReader in the
/// owning module's reader implementation to avoid duplicating the candidate/interview/offer/hire
/// counting logic.
/// Internal recruitment Ticket 6: <c>isInternal</c> optionally restricts the counted applications —
/// null = all, true = only internal applications (Application.Source == Internal), false = all
/// other applications. Internal status is never inferred from a candidate's employee link.
/// </summary>
public interface IVacancyPerformanceReader
{
    Task<IReadOnlyList<VacancyPerformanceItem>> GetVacancyPerformanceAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        bool? isInternal,
        CancellationToken cancellationToken);
}

public sealed record VacancyPerformanceItem(
    Guid VacancyId,
    string VacancyTitle,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    int DaysOpen,
    int CandidateCount,
    int InterviewCount,
    int OfferCount,
    DateOnly? HireDate);

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Per-vacancy performance metrics for the Vacancy Performance Report (OBT-710), as owned by
/// HR.Modules.Recruitment. Shares underlying query logic with IRecruitmentPipelineReader in the
/// owning module's reader implementation to avoid duplicating the applicant/interview/offer/hire
/// counting logic.
/// </summary>
public interface IVacancyPerformanceReader
{
    Task<IReadOnlyList<VacancyPerformanceItem>> GetVacancyPerformanceAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken);
}

public sealed record VacancyPerformanceItem(
    Guid VacancyId,
    string VacancyTitle,
    DateOnly? OpenedAt,
    DateOnly? ClosedAt,
    int DaysOpen,
    int ApplicantCount,
    int InterviewCount,
    int OfferCount,
    DateOnly? HireDate);

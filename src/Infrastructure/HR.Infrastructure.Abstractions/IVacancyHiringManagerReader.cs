namespace HR.Infrastructure.Abstractions;

public interface IVacancyHiringManagerReader
{
    /// <summary>
    /// Resolves the hiring manager's employee ID for the vacancy associated with the given
    /// interview (Interview -> Application -> Vacancy -> HiringManagerId), or null if the
    /// interview cannot be found within the company.
    /// </summary>
    Task<Guid?> GetHiringManagerIdForInterviewAsync(
        Guid companyId,
        Guid interviewId,
        CancellationToken cancellationToken);
}

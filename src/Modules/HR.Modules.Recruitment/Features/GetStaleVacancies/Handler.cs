using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetStaleVacancies;

internal sealed class GetStaleVacanciesHandler(RecruitmentDbContext db, IClock clock)
{
    private const int DefaultStaleAfterDays = 14;

    public async Task<GetStaleVacanciesResponse> HandleAsync(
        GetStaleVacanciesRequest request,
        CancellationToken cancellationToken)
    {
        var staleAfterDays = request.StaleAfterDays is > 0 ? request.StaleAfterDays.Value : DefaultStaleAfterDays;
        var now = clock.UtcNowOffset();
        var cutoff = now.AddDays(-staleAfterDays);

        var vacancies = await db.Vacancies
            .AsNoTracking()
            .Where(v => v.CompanyId == request.CompanyId && v.Status == VacancyStatus.Open)
            .ToListAsync(cancellationToken);

        if (vacancies.Count == 0)
            return new GetStaleVacanciesResponse([]);

        var vacancyIds = vacancies.Select(v => v.Id).ToList();

        // "Activity" = an application being created or updated (covers new applications, stage
        // moves, interview outcomes, etc. since Application.UpdatedAt advances on every transition).
        var lastActivityByVacancy = await db.Applications
            .AsNoTracking()
            .Where(a => vacancyIds.Contains(a.VacancyId))
            .GroupBy(a => a.VacancyId)
            .Select(g => new { VacancyId = g.Key, LastActivityAt = g.Max(a => a.UpdatedAt) })
            .ToDictionaryAsync(x => x.VacancyId, x => x.LastActivityAt, cancellationToken);

        var items = vacancies
            .Select(v =>
            {
                var lastActivityAt = lastActivityByVacancy.TryGetValue(v.Id, out var la) ? la : (DateTimeOffset?)null;
                // No applications at all yet — treat the vacancy's own opening date as the baseline.
                var referenceDate = lastActivityAt ?? (v.OpenedAt.HasValue
                    ? new DateTimeOffset(v.OpenedAt.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    : v.CreatedAt);
                var daysSinceActivity = (int)(now - referenceDate).TotalDays;
                return (Vacancy: v, LastActivityAt: lastActivityAt, ReferenceDate: referenceDate, DaysSinceActivity: daysSinceActivity);
            })
            .Where(x => x.ReferenceDate < cutoff)
            .OrderByDescending(x => x.DaysSinceActivity)
            .Select(x => new StaleVacancyItem(
                x.Vacancy.Id,
                x.Vacancy.Title,
                x.Vacancy.OpenedAt,
                x.LastActivityAt,
                x.DaysSinceActivity))
            .ToList();

        return new GetStaleVacanciesResponse(items);
    }
}

using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetStaleVacancies;

internal sealed class GetStaleVacanciesHandler(
    RecruitmentDbContext db, IClock clock, IPositionProfileReader positionProfileReader)
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

        // Batch cross-module read for effective (AdvertTitle ?? PositionProfile.Title) display titles —
        // same pattern as ListVacanciesHandler.
        var positionProfileIds = vacancies
            .Select(v => v.PositionProfileId)
            .Distinct()
            .ToList();

        var positionProfilesById = (positionProfileIds.Count > 0
                ? await positionProfileReader.GetSummariesAsync(request.CompanyId, positionProfileIds, cancellationToken)
                : [])
            .ToDictionary(p => p.Id);

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
            .Select(x =>
            {
                var positionProfile = positionProfilesById.GetValueOrDefault(x.Vacancy.PositionProfileId);

                return new StaleVacancyItem(
                    x.Vacancy.Id,
                    x.Vacancy.AdvertTitle ?? positionProfile?.Title ?? "(untitled)",
                    x.Vacancy.OpenedAt,
                    x.LastActivityAt,
                    x.DaysSinceActivity);
            })
            .ToList();

        return new GetStaleVacanciesResponse(items);
    }
}

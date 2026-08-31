using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.DashboardMetrics;

/// <summary>
/// DSH-04: a live application included in one of the recruitment dashboard metric counts. The same
/// row shape is used for every metric's drill-down list so the dashboard can render them uniformly.
/// </summary>
internal sealed record RecruitmentMetricApplicationItem(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid VacancyId,
    string VacancyTitle,
    Guid StageId,
    string StageName,
    DateTimeOffset AppliedAt);

/// <summary>
/// Shared projection used by the DSH-04 metric handlers ("New applications", "Candidates in progress",
/// "Offers awaiting response"). Turns a filtered <c>Applications</c> query into drill-down rows,
/// resolving the display vacancy title via the same cross-module <see cref="IPositionProfileReader"/>
/// batch read that <c>ListVacanciesHandler</c> / <c>GetApplicationsByStatusHandler</c> use
/// (<c>AdvertTitle ?? PositionProfile.Title</c>). This is a feature-local mapper, not a repository:
/// callers own their <c>WHERE</c> clause and pass it in.
/// </summary>
internal static class MetricApplicationItemMapper
{
    public static async Task<IReadOnlyList<RecruitmentMetricApplicationItem>> MapAsync(
        RecruitmentDbContext db,
        IPositionProfileReader positionProfileReader,
        Guid companyId,
        IQueryable<Domain.Application> applications,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from a in applications
                join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
                join v in db.Vacancies.AsNoTracking() on a.VacancyId equals v.Id
                join s in db.RecruitmentStages.AsNoTracking() on a.CurrentStageId equals s.Id
                orderby a.AppliedAt descending
                select new
                {
                    ApplicationId = a.Id,
                    CandidateId = c.Id,
                    CandidateName = c.FirstName + " " + c.LastName,
                    c.Email,
                    VacancyId = v.Id,
                    v.AdvertTitle,
                    v.PositionProfileId,
                    StageId = s.Id,
                    StageName = s.Name,
                    a.AppliedAt,
                })
            .ToListAsync(cancellationToken);

        var positionProfileIds = rows.Select(r => r.PositionProfileId).Distinct().ToList();

        var positionProfilesById = (positionProfileIds.Count > 0
                ? await positionProfileReader.GetSummariesAsync(companyId, positionProfileIds, cancellationToken)
                : [])
            .ToDictionary(p => p.Id);

        return rows
            .Select(r => new RecruitmentMetricApplicationItem(
                r.ApplicationId,
                r.CandidateId,
                r.CandidateName,
                r.Email,
                r.VacancyId,
                r.AdvertTitle ?? positionProfilesById.GetValueOrDefault(r.PositionProfileId)?.Title ?? "(untitled)",
                r.StageId,
                r.StageName,
                r.AppliedAt))
            .ToList();
    }
}

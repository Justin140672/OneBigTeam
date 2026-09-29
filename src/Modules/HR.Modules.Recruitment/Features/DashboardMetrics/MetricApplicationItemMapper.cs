using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.DashboardMetrics;

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

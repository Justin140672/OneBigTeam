using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetApplicationsByStatus;

internal sealed class GetApplicationsByStatusHandler(
    RecruitmentDbContext dbContext, IPositionProfileReader positionProfileReader)
{
    public async Task<GetApplicationsByStatusResponse> HandleAsync(
        GetApplicationsByStatusRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from a in dbContext.Applications.AsNoTracking()
                join c in dbContext.Candidates.AsNoTracking() on a.CandidateId equals c.Id
                join v in dbContext.Vacancies.AsNoTracking() on a.VacancyId equals v.Id
                where a.CompanyId == request.CompanyId && a.CurrentStageId == request.StageId
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
                    a.AppliedAt,
                })
            .ToListAsync(cancellationToken);

        // Batch cross-module read for the pure-display "VacancyTitle" (this feature is not about
        // distinguishing advert vs. Position Profile — it just needs a title that's always populated),
        // resolved as AdvertTitle ?? PositionProfile.Title — same pattern as ListVacanciesHandler.
        var positionProfileIds = rows
            .Select(r => r.PositionProfileId)
            .Distinct()
            .ToList();

        var positionProfilesById = (positionProfileIds.Count > 0
                ? await positionProfileReader.GetSummariesAsync(request.CompanyId, positionProfileIds, cancellationToken)
                : [])
            .ToDictionary(p => p.Id);

        var items = rows
            .Select(r =>
            {
                var positionProfile = positionProfilesById.GetValueOrDefault(r.PositionProfileId);

                return new ApplicationByStatusItem(
                    r.ApplicationId,
                    r.CandidateId,
                    r.CandidateName,
                    r.Email,
                    r.VacancyId,
                    r.AdvertTitle ?? positionProfile?.Title ?? "(untitled)",
                    r.AppliedAt);
            })
            .ToList();

        return new GetApplicationsByStatusResponse(items);
    }
}

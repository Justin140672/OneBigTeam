using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Employees.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.SearchApplications;

internal sealed class SearchApplicationsHandler(
    RecruitmentDbContext dbContext,
    IPositionProfileReader positionProfileReader)
{
    public async Task<SearchApplicationsResponse> HandleAsync(
        SearchApplicationsRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize   = request.PageSize   < 1 ? 20 : request.PageSize;

        var query =
            from a in dbContext.Applications.AsNoTracking()
            join c in dbContext.Candidates.AsNoTracking() on a.CandidateId equals c.Id
            join v in dbContext.Vacancies.AsNoTracking()  on a.VacancyId   equals v.Id
            where a.CompanyId == request.CompanyId
            select new
            {
                ApplicationId = a.Id,
                CandidateId   = c.Id,
                CandidateName = c.FirstName + " " + c.LastName,
                c.Email,
                VacancyId     = v.Id,
                v.AdvertTitle,
                v.PositionProfileId,
                a.CurrentStageId,
                a.AppliedAt,
                a.SourceExternalRecruiterId,
                // Internal recruitment Ticket 6: Source is the authoritative internal indicator. The
                // employee link is only surfaced for Internal applications — Candidate.EmployeeId is
                // also set on external candidates once hired and must not make them look internal.
                a.Source,
                IsInternal         = a.Source == Domain.ApplicationSource.Internal,
                InternalEmployeeId = a.Source == Domain.ApplicationSource.Internal ? c.EmployeeId : null,
                IsWithdrawn        = a.WithdrawnAt != null,
                CurrentStageName   = dbContext.RecruitmentStages
                    .Where(s => s.Id == a.CurrentStageId && s.CompanyId == a.CompanyId)
                    .Select(s => s.Name)
                    .FirstOrDefault(),
            };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(r =>
                r.CandidateName.ToLower().Contains(search) ||
                r.Email.ToLower().Contains(search) ||
                (r.AdvertTitle != null && r.AdvertTitle.ToLower().Contains(search)));
        }

        if (request.VacancyId is not null)
            query = query.Where(r => r.VacancyId == request.VacancyId);

        if (request.StageId is not null)
            query = query.Where(r => r.CurrentStageId == request.StageId);

        if (request.ExternalRecruiterId is not null)
            query = query.Where(r => r.SourceExternalRecruiterId == request.ExternalRecruiterId);

        if (request.CandidateId is not null)
            query = query.Where(r => r.CandidateId == request.CandidateId);

        if (request.IsInternal == true)
            query = query.Where(r => r.Source == Domain.ApplicationSource.Internal);
        else if (request.IsInternal == false)
            query = query.Where(r => r.Source == null || r.Source != Domain.ApplicationSource.Internal);

        if (request.AppliedFrom is not null)
            query = query.Where(r => r.AppliedAt >= request.AppliedFrom);

        if (request.AppliedTo is not null)
            query = query.Where(r => r.AppliedAt <= request.AppliedTo);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(r => r.AppliedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var positionProfileIds = rows
            .Select(r => r.PositionProfileId)
            .Distinct()
            .ToList();

        var positionProfilesById = (positionProfileIds.Count > 0
                ? await positionProfileReader.GetSummariesAsync(request.CompanyId, positionProfileIds, cancellationToken)
                : [])
            .ToDictionary(p => p.Id);

        var states = await InterviewStageStateReader.LoadAsync(
            dbContext, request.CompanyId, rows.Select(r => (r.ApplicationId, r.CurrentStageId)).ToList(), cancellationToken);

        var items = rows
            .Select(r =>
            {
                var pos = positionProfilesById.GetValueOrDefault(r.PositionProfileId);
                return new ApplicationSearchItem(
                    r.ApplicationId,
                    r.CandidateId,
                    r.CandidateName,
                    r.Email,
                    r.VacancyId,
                    r.AdvertTitle ?? pos?.Title ?? "(untitled)",
                    r.CurrentStageId,
                    r.AppliedAt,
                    r.CurrentStageName,
                    r.IsWithdrawn,
                    r.IsInternal,
                    r.InternalEmployeeId,
                    states.GetValueOrDefault(r.ApplicationId)?.CurrentStageHasPendingInterview ?? false,
                    states.GetValueOrDefault(r.ApplicationId)?.PendingInterviewId,
                    states.GetValueOrDefault(r.ApplicationId)?.LatestOutcome?.ToString(),
                    states.GetValueOrDefault(r.ApplicationId)?.HasNextInterviewStage ?? false,
                    states.GetValueOrDefault(r.ApplicationId)?.NextInterviewStageId,
                    states.GetValueOrDefault(r.ApplicationId)?.NextInterviewStageName,
                    states.GetValueOrDefault(r.ApplicationId)?.AllRequiredInterviewStagesPassed ?? true);
            })
            .ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new SearchApplicationsResponse(items, totalCount, pageNumber, pageSize, totalPages);
    }
}

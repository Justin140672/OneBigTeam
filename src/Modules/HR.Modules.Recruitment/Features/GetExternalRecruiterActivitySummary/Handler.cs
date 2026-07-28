using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiterActivitySummary;

internal sealed class GetExternalRecruiterActivitySummaryHandler(RecruitmentDbContext db)
{
    public async Task<Result<GetExternalRecruiterActivitySummaryResponse>> HandleAsync(
        GetExternalRecruiterActivitySummaryRequest request,
        CancellationToken cancellationToken)
    {
        var recruiter = await db.ExternalRecruiters
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Id == request.ExternalRecruiterId && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (recruiter is null)
            return Result.Failure<GetExternalRecruiterActivitySummaryResponse>(
                Error.NotFound($"External recruiter '{request.ExternalRecruiterId}' was not found."));

        // Ticket #81 scope correction: VacancyRecruiterAssignment (many-to-many with assignment
        // history) has been removed — Vacancy.AssignedRecruiterId is now a single optional field.
        // "Current vacancies" is straightforward: vacancies still open/on-hold with this recruiter
        // currently assigned. "Previous vacancies" no longer has a true assignment-removal history to
        // draw on (there is no more "unassigned but was assigned" record once AssignedRecruiterId is
        // cleared or repointed to a different recruiter) — as a judgement call, it is redefined here as
        // vacancies where this recruiter is *still* the assigned recruiter but the vacancy itself has
        // reached a terminal status (Closed/Cancelled). This is a real behaviour change: a recruiter
        // that was assigned to a vacancy and then explicitly reassigned/cleared before the vacancy
        // closed will no longer appear anywhere in this summary, whereas under the old model it would
        // still show up in "previous vacancies" via its deactivated assignment row. There is no
        // DateInstructed on Vacancy, so that column is no longer meaningful here — OpenedAt is used
        // instead as the closest available date signal.
        var recruiterVacancies = db.Vacancies
            .AsNoTracking()
            .Where(v => v.AssignedRecruiterId == request.ExternalRecruiterId && v.CompanyId == request.CompanyId);

        var currentVacancies = await recruiterVacancies
            .Where(v => v.Status != VacancyStatus.Closed && v.Status != VacancyStatus.Cancelled)
            .Select(v => new VacancyActivityItem(v.Id, v.AdvertTitle, v.Status, v.OpenedAt))
            .ToListAsync(cancellationToken);

        var previousVacancies = await recruiterVacancies
            .Where(v => v.Status == VacancyStatus.Closed || v.Status == VacancyStatus.Cancelled)
            .Select(v => new VacancyActivityItem(v.Id, v.AdvertTitle, v.Status, v.OpenedAt))
            .ToListAsync(cancellationToken);

        var candidatesIntroducedCount = await db.Set<Application>()
            .AsNoTracking()
            .CountAsync(a => a.CompanyId == request.CompanyId && a.SourceExternalRecruiterId == request.ExternalRecruiterId, cancellationToken);

        // Ticket #99: "hired" now means the application's current stage has TerminalOutcome == Hired,
        // rather than Status == ApplicationStatus.Hired.
        var candidatesHiredCount = await (
            from a in db.Set<Application>().AsNoTracking()
            join s in db.RecruitmentStages.AsNoTracking() on a.CurrentStageId equals s.Id
            where a.CompanyId == request.CompanyId
                && a.SourceExternalRecruiterId == request.ExternalRecruiterId
                && s.TerminalOutcome == RecruitmentStageTerminalOutcome.Hired
            select a.Id)
            .CountAsync(cancellationToken);

        return Result.Success(new GetExternalRecruiterActivitySummaryResponse(
            recruiter.Id,
            recruiter.AgencyName,
            currentVacancies,
            previousVacancies,
            candidatesIntroducedCount,
            candidatesHiredCount));
    }
}

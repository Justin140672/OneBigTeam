using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed class ListApplicationsForVacancyHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListApplicationsForVacancyResponse>> HandleAsync(
        ListApplicationsForVacancyRequest request,
        CancellationToken cancellationToken)
    {
        var query =
            from a in db.Applications.AsNoTracking()
            join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
            where a.CompanyId == request.CompanyId
               && a.VacancyId == request.VacancyId
            select new { a, c };

        if (request.StageId.HasValue)
            query = query.Where(x => x.a.CurrentStageId == request.StageId.Value);

        // Internal recruitment Ticket 6: Source is the authoritative internal indicator — never
        // Candidate.EmployeeId, which is also set on external candidates once they are hired.
        if (request.IsInternal == true)
            query = query.Where(x => x.a.Source == Domain.ApplicationSource.Internal);
        else if (request.IsInternal == false)
            query = query.Where(x => x.a.Source == null || x.a.Source != Domain.ApplicationSource.Internal);

        var rows = await query
            .OrderByDescending(x => x.a.AppliedAt)
            .Select(x => new
            {
                x.a.Id,
                x.a.CandidateId,
                x.c.FirstName,
                x.c.LastName,
                x.c.Email,
                x.a.CurrentStageId,
                x.a.InterviewOutcome,
                IsWithdrawn = x.a.WithdrawnAt != null,
                x.a.AppliedAt,
                x.a.OfferResponseStatus,
                x.a.OfferedSalary,
                x.a.OfferedStartDate,
                IsInternal = x.a.Source == Domain.ApplicationSource.Internal,
                InternalEmployeeId = x.a.Source == Domain.ApplicationSource.Internal ? x.c.EmployeeId : null,
                x.a.AppointmentStatus,
                x.a.AppointmentEffectiveDate,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new ApplicationListItem(
                r.Id,
                r.CandidateId,
                r.FirstName,
                r.LastName,
                r.Email,
                r.CurrentStageId,
                r.InterviewOutcome,
                r.IsWithdrawn,
                r.AppliedAt,
                r.OfferResponseStatus?.ToString(),
                r.OfferedSalary,
                r.OfferedStartDate,
                r.IsInternal,
                r.InternalEmployeeId,
                r.AppointmentStatus?.ToString(),
                r.AppointmentEffectiveDate))
            .ToList();

        return Result.Success(new ListApplicationsForVacancyResponse(items));
    }
}

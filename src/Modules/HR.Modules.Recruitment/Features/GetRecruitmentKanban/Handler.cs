using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

internal sealed class GetRecruitmentKanbanHandler(RecruitmentDbContext db, IPositionProfileReader positionProfileReader)
{
    public async Task<Result<GetRecruitmentKanbanResponse>> HandleAsync(
        GetRecruitmentKanbanRequest request,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<GetRecruitmentKanbanResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var positionProfile = await positionProfileReader.GetSummaryAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

        var vacancyTitle = vacancy.AdvertTitle ?? positionProfile?.Title ?? "(untitled)";

        string? assignedRecruiterAgencyName = null;
        if (vacancy.AssignedRecruiterId is { } assignedRecruiterId)
        {
            assignedRecruiterAgencyName = await db.ExternalRecruiters
                .AsNoTracking()
                .Where(r => r.Id == assignedRecruiterId)
                .Select(r => r.AgencyName)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var stages = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId && s.IsActive)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var candidates = await (
            from a in db.Applications.AsNoTracking()
            join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
            where a.CompanyId == request.CompanyId && a.VacancyId == request.VacancyId
            orderby a.AppliedAt
            select new
            {
                a.Id,
                a.CandidateId,
                c.FirstName,
                c.LastName,
                a.CurrentStageId,
                a.WithdrawnAt,
                a.AppliedAt,
                // Internal recruitment Ticket 6: Source is the authoritative internal indicator; the
                // employee link is only surfaced for Internal applications (a hired external candidate
                // also has Candidate.EmployeeId set and must not appear as internal).
                IsInternal = a.Source == Domain.ApplicationSource.Internal,
                InternalEmployeeId = a.Source == Domain.ApplicationSource.Internal ? c.EmployeeId : null,
            })
            .ToListAsync(cancellationToken);

        var groupedByStage = candidates
            .GroupBy(a => a.CurrentStageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var columns = stages
            .Select(stage =>
            {
                var items = groupedByStage.TryGetValue(stage.Id, out var group) ? group : [];

                var summaries = items
                    .Select(a => new KanbanCandidateSummary(
                        a.Id,
                        a.CandidateId,
                        a.FirstName,
                        a.LastName,
                        null,
                        stage.Id,
                        stage.Name,
                        a.WithdrawnAt is not null,
                        a.AppliedAt,
                        vacancy.AssignedRecruiterId,
                        assignedRecruiterAgencyName,
                        vacancyTitle,
                        a.IsInternal,
                        a.InternalEmployeeId))
                    .ToList();

                return new KanbanColumn(stage.Id, stage.Name, stage.IsTerminal, summaries.Count, summaries);
            })
            .ToList();

        return Result.Success(new GetRecruitmentKanbanResponse(vacancy.Id, vacancyTitle, columns));
    }
}

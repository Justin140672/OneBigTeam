using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

/// <summary>
/// Ticket #63: Kanban read model for a single vacancy's applicant pipeline, grouped by stage in
/// pipeline order. Ticket #99: columns are now the company's own active RecruitmentStage rows (in
/// DisplayOrder) instead of the fixed eight ApplicationStatus values — the board layout is stable
/// across vacancies within the same company, but differs between companies with different stage
/// configurations. Withdrawn applications are not given a separate column (no "Withdrawn" stage
/// exists — see Application.WithdrawnAt's remarks) — they remain visible under whatever stage they
/// were on when withdrawn, flagged via KanbanApplicantSummary.IsWithdrawn so the UI can grey them out.
/// </summary>
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

        // Ticket #81: AssignedRecruiterId now references ExternalRecruiter (an external agency), not
        // an Employee — resolved here (same module/schema, direct EF Core access) rather than by the
        // UI looking it up against the employee list, which is what happened before this change.
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

        var applicants = await (
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
            })
            .ToListAsync(cancellationToken);

        var groupedByStage = applicants
            .GroupBy(a => a.CurrentStageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var columns = stages
            .Select(stage =>
            {
                var items = groupedByStage.TryGetValue(stage.Id, out var group) ? group : [];

                var summaries = items
                    .Select(a => new KanbanApplicantSummary(
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
                        vacancyTitle))
                    .ToList();

                return new KanbanColumn(stage.Id, stage.Name, stage.IsTerminal, summaries.Count, summaries);
            })
            .ToList();

        return Result.Success(new GetRecruitmentKanbanResponse(vacancy.Id, vacancyTitle, columns));
    }
}

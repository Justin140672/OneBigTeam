using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

/// <summary>
/// Ticket #63: Kanban read model for a single vacancy's applicant pipeline, grouped by stage in
/// pipeline order. Judgement call: Rejected/Withdrawn are included as trailing terminal columns
/// (rather than excluded entirely) so recruiters can still see/find applicants who left the active
/// pipeline without a separate query — all eight ApplicationStatus values always appear as columns,
/// even when empty, so the board layout is stable across vacancies.
/// </summary>
internal sealed class GetRecruitmentKanbanHandler(RecruitmentDbContext db, IPositionProfileReader positionProfileReader)
{
    private static readonly ApplicationStatus[] ColumnOrder =
    [
        ApplicationStatus.Applied,
        ApplicationStatus.Screening,
        ApplicationStatus.InterviewScheduled,
        ApplicationStatus.Interviewed,
        ApplicationStatus.Offered,
        ApplicationStatus.Hired,
        ApplicationStatus.Rejected,
        ApplicationStatus.Withdrawn,
    ];

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
                a.Status,
                a.AppliedAt,
            })
            .ToListAsync(cancellationToken);

        var groupedByStage = applicants
            .GroupBy(a => a.Status)
            .ToDictionary(g => g.Key, g => g.ToList());

        var columns = ColumnOrder
            .Select(stage =>
            {
                var items = groupedByStage.TryGetValue(stage, out var group) ? group : [];

                var summaries = items
                    .Select(a => new KanbanApplicantSummary(
                        a.Id,
                        a.CandidateId,
                        a.FirstName,
                        a.LastName,
                        null,
                        a.Status,
                        a.AppliedAt,
                        vacancy.AssignedRecruiterId,
                        assignedRecruiterAgencyName,
                        vacancyTitle))
                    .ToList();

                return new KanbanColumn(stage, summaries.Count, summaries);
            })
            .ToList();

        return Result.Success(new GetRecruitmentKanbanResponse(vacancy.Id, vacancyTitle, columns));
    }
}

using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListRecruitmentStages;

// Returns every stage (active and inactive) ordered by DisplayOrder — this is the admin settings
// screen for ticket #97, so inactive stages must remain visible/editable (just not selectable for new
// application placement/moves), unlike GetRecruitmentKanban/GetPipelineSummary which only show active
// stages.
internal sealed class ListRecruitmentStagesHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListRecruitmentStagesResponse>> HandleAsync(
        ListRecruitmentStagesRequest request,
        CancellationToken cancellationToken)
    {
        var items = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new RecruitmentStageListItem(
                s.Id,
                s.Name,
                s.DisplayOrder,
                s.IsActive,
                s.IsTerminal,
                s.TerminalOutcome,
                s.Purpose))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListRecruitmentStagesResponse(items));
    }
}

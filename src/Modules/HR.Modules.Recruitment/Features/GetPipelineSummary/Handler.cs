using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetPipelineSummary;

// Ticket #99: the funnel is now the company's own active, non-terminal RecruitmentStage rows in
// DisplayOrder, replacing the old fixed six-value ApplicationStatus funnel. Terminal stages (Hired /
// Rejected) are excluded, same as the old funnel excluded Hired/Rejected/Withdrawn — those remain
// visible via GetRecruitmentKanban instead.
internal sealed class GetPipelineSummaryHandler(RecruitmentDbContext dbContext)
{
    public async Task<GetPipelineSummaryResponse> HandleAsync(
        GetPipelineSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var stages = await dbContext.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId && s.IsActive && !s.IsTerminal)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var counts = await dbContext.Applications
            .AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId && a.WithdrawnAt == null)
            .GroupBy(a => a.CurrentStageId)
            .Select(g => new { StageId = g.Key, ApplicationCount = g.Count() })
            .ToDictionaryAsync(x => x.StageId, x => x.ApplicationCount, cancellationToken);

        var items = stages
            .Select(stage => new PipelineSummaryItem(stage.Id, stage.Name, counts.GetValueOrDefault(stage.Id, 0)))
            .ToList();

        return new GetPipelineSummaryResponse(items);
    }
}

using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetPipelineSummary;

internal sealed class GetPipelineSummaryHandler(RecruitmentDbContext dbContext)
{
    // Funnel stage order — excludes Rejected/Withdrawn, which fall outside the active pipeline.
    private static readonly ApplicationStatus[] FunnelStages =
    [
        ApplicationStatus.Applied,
        ApplicationStatus.Screening,
        ApplicationStatus.InterviewScheduled,
        ApplicationStatus.Interviewed,
        ApplicationStatus.Offered,
        ApplicationStatus.Hired,
    ];

    public async Task<GetPipelineSummaryResponse> HandleAsync(
        GetPipelineSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.Applications
            .AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId && FunnelStages.Contains(a.Status))
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, ApplicationCount = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.ApplicationCount, cancellationToken);

        var items = FunnelStages
            .Select(stage => new PipelineSummaryItem(stage.ToString(), counts.GetValueOrDefault(stage, 0)))
            .ToList();

        return new GetPipelineSummaryResponse(items);
    }
}

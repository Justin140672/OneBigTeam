using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Features.DashboardMetrics;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetCandidatesInProgressMetric;

/// <summary>
/// DSH-04 — "Candidates in progress".
///
/// <para><b>Business definition.</b> The count of live applications for the company: not withdrawn by
/// the candidate, and currently in a non-terminal stage (i.e. not yet Hired or Rejected). This is the
/// honest "how many people are we actively recruiting right now" figure and is independent of stage
/// ordering or naming — it is driven only by the <c>is_terminal</c> flag and <c>withdrawn_at</c>.</para>
///
/// Company scope enforced by the <c>{companyId}</c> route + <c>company_id</c> filter on every query.
/// </summary>
internal sealed class GetCandidatesInProgressMetricHandler(
    RecruitmentDbContext db, IPositionProfileReader positionProfileReader)
{
    public async Task<GetCandidatesInProgressMetricResponse> HandleAsync(
        GetCandidatesInProgressMetricRequest request,
        CancellationToken cancellationToken)
    {
        var nonTerminalStageIds = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId && !s.IsTerminal)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var query = db.Applications
            .AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId
                     && a.WithdrawnAt == null
                     && nonTerminalStageIds.Contains(a.CurrentStageId));

        var items = await MetricApplicationItemMapper.MapAsync(
            db, positionProfileReader, request.CompanyId, query, cancellationToken);

        return new GetCandidatesInProgressMetricResponse(items.Count, items);
    }
}

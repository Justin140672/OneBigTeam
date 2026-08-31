using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DashboardMetrics;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetNewApplicationsMetric;

/// <summary>
/// DSH-04 — "New applications".
///
/// <para><b>Business definition.</b> The count of live applications (not withdrawn, not in a terminal
/// stage) that are sitting in a stage the company has explicitly flagged with the
/// <see cref="RecruitmentStagePurpose.NewApplication"/> purpose. This is a deliberate configuration
/// choice, never inferred from stage ordering.</para>
///
/// <para><b>Fallback.</b> If the company has configured no stage with that purpose, the metric falls
/// back to a purely time-based definition: live applications received in the last
/// <c>NewWithinDays</c> days (default 14). <see cref="GetNewApplicationsMetricResponse.DefinedByStagePurpose"/>
/// reports which definition was used so the UI can prompt the company to configure a stage.</para>
///
/// Company scope is enforced by the <c>{companyId}</c> route (validated against the caller's tenant by
/// <c>TenantRouteAuthorizationMiddleware</c>) and a <c>company_id</c> filter on every query.
/// </summary>
internal sealed class GetNewApplicationsMetricHandler(
    RecruitmentDbContext db, IClock clock, IPositionProfileReader positionProfileReader)
{
    private const int DefaultNewWithinDays = 14;

    public async Task<GetNewApplicationsMetricResponse> HandleAsync(
        GetNewApplicationsMetricRequest request,
        CancellationToken cancellationToken)
    {
        var newApplicationStageIds = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId
                     && s.IsActive
                     && !s.IsTerminal
                     && s.Purpose == RecruitmentStagePurpose.NewApplication)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var definedByStagePurpose = newApplicationStageIds.Count > 0;

        // Non-terminal stage ids: the fallback still excludes applications that have already reached a
        // terminal (Hired/Rejected) stage even if they were created recently.
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

        if (definedByStagePurpose)
        {
            query = query.Where(a => newApplicationStageIds.Contains(a.CurrentStageId));
        }
        else
        {
            var withinDays = request.NewWithinDays is > 0 ? request.NewWithinDays.Value : DefaultNewWithinDays;
            var cutoff = clock.UtcNowOffset().AddDays(-withinDays);
            query = query.Where(a => a.AppliedAt >= cutoff);
        }

        var items = await MetricApplicationItemMapper.MapAsync(
            db, positionProfileReader, request.CompanyId, query, cancellationToken);

        return new GetNewApplicationsMetricResponse(items.Count, definedByStagePurpose, items);
    }
}

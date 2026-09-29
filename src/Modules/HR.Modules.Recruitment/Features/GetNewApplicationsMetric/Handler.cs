using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DashboardMetrics;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetNewApplicationsMetric;

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

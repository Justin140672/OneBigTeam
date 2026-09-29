using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DashboardMetrics;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetOffersAwaitingResponseMetric;

internal sealed class GetOffersAwaitingResponseMetricHandler(
    RecruitmentDbContext db, IPositionProfileReader positionProfileReader)
{
    public async Task<GetOffersAwaitingResponseMetricResponse> HandleAsync(
        GetOffersAwaitingResponseMetricRequest request,
        CancellationToken cancellationToken)
    {
        var offerStageIds = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId
                     && s.IsActive
                     && !s.IsTerminal
                     && s.Purpose == RecruitmentStagePurpose.Offer)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (offerStageIds.Count == 0)
            return new GetOffersAwaitingResponseMetricResponse(0, OfferStageConfigured: false, []);

        var query = db.Applications
            .AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId
                     && a.WithdrawnAt == null
                     && offerStageIds.Contains(a.CurrentStageId));

        var items = await MetricApplicationItemMapper.MapAsync(
            db, positionProfileReader, request.CompanyId, query, cancellationToken);

        return new GetOffersAwaitingResponseMetricResponse(items.Count, OfferStageConfigured: true, items);
    }
}

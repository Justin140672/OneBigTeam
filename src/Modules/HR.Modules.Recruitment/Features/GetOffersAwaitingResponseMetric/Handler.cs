using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DashboardMetrics;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetOffersAwaitingResponseMetric;

/// <summary>
/// DSH-04 — "Offers awaiting response".
///
/// <para><b>Business definition.</b> The count of live applications (not withdrawn) currently in a
/// stage the company has explicitly flagged with the <see cref="RecruitmentStagePurpose.Offer"/>
/// purpose. If several stages carry that purpose (e.g. "Verbal offer" and "Written offer"),
/// applications in any of them are counted. The offer stage is a deliberate configuration choice —
/// it is never inferred from a stage's position in the pipeline ordering.</para>
///
/// <para>If no stage is flagged with the Offer purpose the count is 0 and
/// <see cref="GetOffersAwaitingResponseMetricResponse.OfferStageConfigured"/> is <c>false</c>.</para>
///
/// Company scope enforced by the <c>{companyId}</c> route + <c>company_id</c> filter on every query.
/// </summary>
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

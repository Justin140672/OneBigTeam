using System.Security.Claims;
using HR.Modules.Reporting.Features.DashboardSummaries;

namespace HR.Modules.Reporting.Features.GetHrDashboardSummary;

/// <summary>
/// DSH-06 HR dashboard summary. Thin wrapper over <see cref="DashboardSummaryComposer"/> — all
/// aggregation, bounding and partial-failure logic lives in the composer; the endpoint handles
/// authorization.
/// </summary>
internal sealed class GetHrDashboardSummaryHandler(DashboardSummaryComposer composer)
{
    public Task<DashboardSummaryResponse> HandleAsync(
        GetHrDashboardSummaryRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
        => composer.ComposeAsync(request.CompanyId, caller, cancellationToken);
}

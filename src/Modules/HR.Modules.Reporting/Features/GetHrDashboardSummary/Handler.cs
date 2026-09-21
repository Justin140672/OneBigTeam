using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.DashboardSummaries;

namespace HR.Modules.Reporting.Features.GetHrDashboardSummary;

/// <summary>
/// DSH-06 HR dashboard summary. Thin wrapper over <see cref="DashboardSummaryComposer"/> — all
/// aggregation, bounding and partial-failure logic lives in the composer; the endpoint handles
/// authorization. Explicitly requests <see cref="WorkloadScope.Hr"/> so providers apply HR's
/// company-wide rules regardless of whether the caller also holds a Manager role.
/// </summary>
internal sealed class GetHrDashboardSummaryHandler(DashboardSummaryComposer composer)
{
    public Task<DashboardSummaryResponse> HandleAsync(
        GetHrDashboardSummaryRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
        => composer.ComposeAsync(request.CompanyId, caller, WorkloadScope.Hr, cancellationToken);
}

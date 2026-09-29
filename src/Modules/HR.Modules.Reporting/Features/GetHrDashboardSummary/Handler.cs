using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.DashboardSummaries;

namespace HR.Modules.Reporting.Features.GetHrDashboardSummary;

internal sealed class GetHrDashboardSummaryHandler(DashboardSummaryComposer composer)
{
    public Task<DashboardSummaryResponse> HandleAsync(
        GetHrDashboardSummaryRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
        => composer.ComposeAsync(request.CompanyId, caller, WorkloadScope.Hr, cancellationToken);
}

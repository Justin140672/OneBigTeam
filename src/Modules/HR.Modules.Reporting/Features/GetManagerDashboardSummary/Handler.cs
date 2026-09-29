using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.DashboardSummaries;

namespace HR.Modules.Reporting.Features.GetManagerDashboardSummary;

internal sealed class GetManagerDashboardSummaryHandler(DashboardSummaryComposer composer)
{
    public Task<DashboardSummaryResponse> HandleAsync(
        GetManagerDashboardSummaryRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
        => composer.ComposeAsync(request.CompanyId, caller, WorkloadScope.Manager, cancellationToken);
}

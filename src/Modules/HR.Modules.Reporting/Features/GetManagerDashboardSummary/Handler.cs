using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.DashboardSummaries;

namespace HR.Modules.Reporting.Features.GetManagerDashboardSummary;

/// <summary>
/// DSH-06 Manager dashboard summary. Thin wrapper over <see cref="DashboardSummaryComposer"/>. There
/// is no managerId parameter: the acting manager is always ICurrentUser. Explicitly requests
/// <see cref="WorkloadScope.Manager"/> so every provider applies manager-team scoping even when the
/// caller ALSO holds an HR role — fixes a bug where a dual HR+Manager caller's Manager dashboard was
/// showing HR's company-wide data because providers re-derived scope from the caller's roles alone.
/// </summary>
internal sealed class GetManagerDashboardSummaryHandler(DashboardSummaryComposer composer)
{
    public Task<DashboardSummaryResponse> HandleAsync(
        GetManagerDashboardSummaryRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
        => composer.ComposeAsync(request.CompanyId, caller, WorkloadScope.Manager, cancellationToken);
}

using System.Security.Claims;
using HR.Modules.Reporting.Features.DashboardSummaries;

namespace HR.Modules.Reporting.Features.GetManagerDashboardSummary;

/// <summary>
/// DSH-06 Manager dashboard summary. Thin wrapper over <see cref="DashboardSummaryComposer"/>. There
/// is no managerId parameter: the acting manager is always ICurrentUser, and every
/// IWorkloadActionProvider already self-scopes a manager caller to their full reporting sub-tree
/// (DSH-02), so there is nothing for this handler to scope.
/// </summary>
internal sealed class GetManagerDashboardSummaryHandler(DashboardSummaryComposer composer)
{
    public Task<DashboardSummaryResponse> HandleAsync(
        GetManagerDashboardSummaryRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
        => composer.ComposeAsync(request.CompanyId, caller, cancellationToken);
}

using FastEndpoints;
using HR.Modules.Reporting.Features.DashboardSummaries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetHrDashboardSummary;

internal sealed class Endpoint(
    GetHrDashboardSummaryHandler handler,
    IAuthorizationService authorizationService)
    : Endpoint<GetHrDashboardSummaryRequest, DashboardSummaryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/dashboards/hr/summary");
        // Shared menu gate (Manager OR HrAdministrator), same as the Workload & HR Actions Report.
        Policies("reporting:view-workload-actions");
    }

    public override async Task HandleAsync(
        GetHrDashboardSummaryRequest request,
        CancellationToken cancellationToken)
    {
        // DSH-06 approved answer #2: the HR dashboard summary is HR-only. Rather than mint a new
        // permission, narrow the shared workload-actions menu gate here with reporting:view-hr.
        if (!(await authorizationService.AuthorizeAsync(User, "reporting:view-hr")).Succeeded)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request, User, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}

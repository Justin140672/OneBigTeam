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
        Policies("reporting:view-workload-actions");
    }

    public override async Task HandleAsync(
        GetHrDashboardSummaryRequest request,
        CancellationToken cancellationToken)
    {
        if (!(await authorizationService.AuthorizeAsync(User, "reporting:view-hr")).Succeeded)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request, User, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}

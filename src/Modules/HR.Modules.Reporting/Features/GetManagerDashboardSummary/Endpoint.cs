using FastEndpoints;
using HR.Modules.Reporting.Features.DashboardSummaries;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetManagerDashboardSummary;

internal sealed class Endpoint(GetManagerDashboardSummaryHandler handler)
    : Endpoint<GetManagerDashboardSummaryRequest, DashboardSummaryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/dashboards/manager/summary");
        Policies("reporting:view-workload-actions");
    }

    public override async Task HandleAsync(
        GetManagerDashboardSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, User, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}

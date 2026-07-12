using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetOrganisationChart;

internal sealed class Endpoint(
    GetOrganisationChartHandler handler) : Endpoint<GetOrganisationChartRequest, GetOrganisationChartResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/organisation-chart");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        GetOrganisationChartRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

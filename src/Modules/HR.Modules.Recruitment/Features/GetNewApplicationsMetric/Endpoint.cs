using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetNewApplicationsMetric;

internal sealed class Endpoint(GetNewApplicationsMetricHandler handler)
    : Endpoint<GetNewApplicationsMetricRequest, GetNewApplicationsMetricResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment/metrics/new-applications");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        GetNewApplicationsMetricRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}

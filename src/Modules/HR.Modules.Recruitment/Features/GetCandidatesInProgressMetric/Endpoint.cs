using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetCandidatesInProgressMetric;

internal sealed class Endpoint(GetCandidatesInProgressMetricHandler handler)
    : Endpoint<GetCandidatesInProgressMetricRequest, GetCandidatesInProgressMetricResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment/metrics/candidates-in-progress");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        GetCandidatesInProgressMetricRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}

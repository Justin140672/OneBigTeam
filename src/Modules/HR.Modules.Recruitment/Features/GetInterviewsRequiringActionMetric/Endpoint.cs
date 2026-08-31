using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetInterviewsRequiringActionMetric;

internal sealed class Endpoint(GetInterviewsRequiringActionMetricHandler handler)
    : Endpoint<GetInterviewsRequiringActionMetricRequest, GetInterviewsRequiringActionMetricResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment/metrics/interviews-requiring-action");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        GetInterviewsRequiringActionMetricRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}

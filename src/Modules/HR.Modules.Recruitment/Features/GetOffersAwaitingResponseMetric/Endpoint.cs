using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetOffersAwaitingResponseMetric;

internal sealed class Endpoint(GetOffersAwaitingResponseMetricHandler handler)
    : Endpoint<GetOffersAwaitingResponseMetricRequest, GetOffersAwaitingResponseMetricResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/recruitment/metrics/offers-awaiting-response");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        GetOffersAwaitingResponseMetricRequest request,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}

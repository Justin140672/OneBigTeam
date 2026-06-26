using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetUpcomingProbationReviews;

internal sealed class Endpoint(
    GetUpcomingProbationReviewsHandler handler) : Endpoint<GetUpcomingProbationReviewsRequest, GetUpcomingProbationReviewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/probation-reviews/upcoming");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        GetUpcomingProbationReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

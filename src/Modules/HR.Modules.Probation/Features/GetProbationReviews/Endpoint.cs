using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetProbationReviews;

internal sealed class Endpoint(
    GetProbationReviewsHandler handler) : Endpoint<GetProbationReviewsRequest, GetProbationReviewsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/probation-records/{probationRecordId:guid}/reviews");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        GetProbationReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

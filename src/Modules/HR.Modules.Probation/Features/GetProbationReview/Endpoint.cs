using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetProbationReview;

internal sealed class Endpoint(
    GetProbationReviewHandler handler) : Endpoint<GetProbationReviewRequest, GetProbationReviewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/probation-reviews/{reviewId:guid}");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        GetProbationReviewRequest request,
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

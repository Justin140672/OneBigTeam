using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetReturnToWorkReview;

internal sealed class Endpoint(
    GetReturnToWorkReviewHandler handler) : Endpoint<GetReturnToWorkReviewRequest, GetReturnToWorkReviewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/return-to-work-reviews/{reviewId:guid}");
        Policies("sickness:review");
    }

    public override async Task HandleAsync(
        GetReturnToWorkReviewRequest request,
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

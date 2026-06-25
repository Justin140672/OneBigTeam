using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed class Endpoint(
    CompleteProbationReviewHandler handler) : Endpoint<CompleteProbationReviewRequest, CompleteProbationReviewResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/probation-records/{probationRecordId:guid}/reviews/{reviewId:guid}/complete");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        CompleteProbationReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

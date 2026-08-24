using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed class Endpoint(
    CompleteProbationReviewHandler handler,
    ICurrentUser currentUser) : Endpoint<CompleteProbationReviewRequest, CompleteProbationReviewResponse>
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
        // PROB-05: the acting decision maker is always resolved server-side from the
        // authenticated caller — never trusted from the request body — so DecisionMakerEmployeeId
        // on the resulting record/audit trail can be trusted.
        if (currentUser.UserId is not { } completedByEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, completedByEmployeeId, cancellationToken);

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

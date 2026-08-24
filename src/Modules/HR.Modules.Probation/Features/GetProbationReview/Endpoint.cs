using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetProbationReview;

internal sealed class Endpoint(
    GetProbationReviewHandler handler,
    ICurrentUser currentUser) : Endpoint<GetProbationReviewRequest, GetProbationReviewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/probation-reviews/{reviewId:guid}");
        Policies("probation:review");
    }

    public override async Task HandleAsync(
        GetProbationReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // PROB-02: reporting-hierarchy/HR authorization for this single-resource read happens
        // inside the handler (it needs the fetched review's EmployeeId), and unauthorized access
        // is reported as NotFound rather than Forbidden so a manager cannot use the response
        // status to distinguish "unrelated review" from "no such review" while guessing ids.
        var result = await handler.HandleAsync(request, callerId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

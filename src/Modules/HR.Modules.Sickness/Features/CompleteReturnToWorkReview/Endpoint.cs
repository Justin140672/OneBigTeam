using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReview;

internal sealed class Endpoint(
    CompleteReturnToWorkReviewHandler handler,
    ICurrentUser currentUser) : Endpoint<CompleteReturnToWorkReviewRequest, CompleteReturnToWorkReviewResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/return-to-work-reviews/{reviewId:guid}/complete");

        // Same "sickness:review" policy (Manager + HrAdministrator) as GetReturnToWorkReview —
        // resource-level authorization (does this caller have a reporting relationship to the
        // reviewed employee?) is applied in the handler, matching the established SICK-02
        // pattern, since the policy alone only proves the caller holds one of those roles.
        Policies("sickness:review");
    }

    public override async Task HandleAsync(
        CompleteReturnToWorkReviewRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's
        // resolved Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale). The
        // reviewer is always resolved server-side from the authenticated caller — never taken
        // from the request body — so ReviewedBy on the completed review can be trusted.
        if (currentUser.UserId is not { } reviewedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, reviewedBy, cancellationToken);

        if (result.IsFailure)
        {
            // Mirrors GetReturnToWorkReview/Endpoint.cs: unauthorized/unrelated access and
            // "doesn't exist" both surface as plain NotFound so a manager cannot use the
            // response to distinguish the two while guessing review ids.
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

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

        Policies("sickness:review");
    }

    public override async Task HandleAsync(
        CompleteReturnToWorkReviewRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } reviewedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            reviewedBy,
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

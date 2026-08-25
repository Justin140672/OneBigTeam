using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.CreateProbationReview;

internal sealed class Endpoint(
    CreateProbationReviewHandler handler,
    ICurrentUser currentUser) : Endpoint<CreateProbationReviewRequest, CreateProbationReviewResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/probation-reviews");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        CreateProbationReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId },
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/probation-reviews/{result.Value.Id}",
            result.Value));
    }
}

using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.RespondToOffer;

internal sealed class Endpoint(RespondToOfferHandler handler, ICurrentUser currentUser)
    : Endpoint<RespondToOfferRequest, RespondToOfferResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/offer/response");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        RespondToOfferRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid performedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            performedBy,
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

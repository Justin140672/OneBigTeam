using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.MoveApplicationForward;

internal sealed class Endpoint(MoveApplicationForwardHandler handler, ICurrentUser currentUser)
    : Endpoint<MoveApplicationForwardRequest, MoveApplicationForwardResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/move-forward");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        MoveApplicationForwardRequest request,
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

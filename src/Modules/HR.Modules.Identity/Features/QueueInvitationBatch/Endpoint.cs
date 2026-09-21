using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.QueueInvitationBatch;

internal sealed class Endpoint(
    QueueInvitationBatchHandler handler,
    ICurrentUser currentUser) : Endpoint<QueueInvitationBatchRequest, QueueInvitationBatchResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/invitation-batches");
        Policies("users:manage");
    }

    public override async Task HandleAsync(QueueInvitationBatchRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            currentUser.UserId,
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}

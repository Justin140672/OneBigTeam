using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.RetryInvitationBatch;

internal sealed class Endpoint(RetryInvitationBatchHandler handler)
    : Endpoint<RetryInvitationBatchRequest, RetryInvitationBatchResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/invitation-batches/{batchId:guid}/retry");
        Policies("users:manage");
    }

    public override async Task HandleAsync(RetryInvitationBatchRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}

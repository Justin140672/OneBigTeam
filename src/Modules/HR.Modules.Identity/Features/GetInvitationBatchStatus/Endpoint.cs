using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetInvitationBatchStatus;

internal sealed class Endpoint(GetInvitationBatchStatusHandler handler)
    : Endpoint<GetInvitationBatchStatusRequest, InvitationBatchStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/invitation-batches/{batchId:guid}");
        Policies("users:manage");
    }

    public override async Task HandleAsync(GetInvitationBatchStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}

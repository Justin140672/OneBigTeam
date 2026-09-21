using FastEndpoints;
using HR.Modules.Identity.Features.GetInvitationBatchStatus;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetLatestInvitationBatch;

internal sealed class Endpoint(GetLatestInvitationBatchHandler handler)
    : Endpoint<GetLatestInvitationBatchRequest, InvitationBatchStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/invitation-batches/latest");
        Policies("users:manage");
    }

    public override async Task HandleAsync(GetLatestInvitationBatchRequest request, CancellationToken cancellationToken)
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

using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

// Assumption: this codebase has no dedicated "internal/staff" role concept yet (see ICurrentUser /
// role policies) — gated behind the "support:manage" permission string following the existing
// "resource:action" convention used throughout (e.g. "employee:manage", "asset:view").
internal sealed class Endpoint(UpdateSupportRequestStatusHandler handler)
    : Endpoint<UpdateSupportRequestStatusRequest, UpdateSupportRequestStatusResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/support/requests/{id:guid}/status");
        Policies("support:manage");
    }

    public override async Task HandleAsync(UpdateSupportRequestStatusRequest request, CancellationToken cancellationToken)
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

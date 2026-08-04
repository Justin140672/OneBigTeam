using FastEndpoints;
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
            var statusCode = result.Error.Code == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict;
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: statusCode));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}

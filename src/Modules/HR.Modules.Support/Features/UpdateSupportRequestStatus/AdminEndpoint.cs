using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

// The only status-transition route: platform administrators only (no tenant role can change status).
// Transition rules, optimistic concurrency (ExpectedVersion / HTTP 409) and the HR-Administrator
// notification fan-out live in UpdateSupportRequestStatusHandler.
internal sealed class AdminEndpoint(UpdateSupportRequestStatusHandler handler)
    : Endpoint<UpdateSupportRequestStatusRequest, UpdateSupportRequestStatusResponse>
{
    public override void Configure()
    {
        Put("/api/admin/companies/{companyId:guid}/support/requests/{id:guid}/status");
        Policies("platform:admin");
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

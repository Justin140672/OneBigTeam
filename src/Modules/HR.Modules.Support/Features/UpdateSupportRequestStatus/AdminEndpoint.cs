using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

// Ticket 17: platform-support surface for HR.Admin.Web — see ListSupportRequests/AdminEndpoint.cs
// remarks. Reuses UpdateSupportRequestStatusHandler unchanged, so transition rules, optimistic
// concurrency (ExpectedVersion / HTTP 409) and the HR-Administrator notification fan-out are
// identical to the tenant "support:manage" endpoint — only the authorization gate differs.
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

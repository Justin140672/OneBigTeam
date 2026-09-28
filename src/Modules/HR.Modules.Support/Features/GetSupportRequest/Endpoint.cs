using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.GetSupportRequest;

internal sealed class Endpoint(GetSupportRequestHandler handler, ICurrentUser currentUser)
    : Endpoint<GetSupportRequestRequest, GetSupportRequestResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/support/requests/{id:guid}");
        Policies("support:request");
    }

    public override async Task HandleAsync(GetSupportRequestRequest request, CancellationToken cancellationToken)
    {
        // Verify the caller belongs to the company in the route
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // Fetch the request to check ownership
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status404NotFound));
            return;
        }

        // Self-service access: requestor can view their own request, or HR admin can view any request
        var supportRequest = result.Value!;
        if (currentUser.UserId is Guid userId && userId == supportRequest.SubmittedByUserId)
        {
            // Requestor viewing their own request
            await Send.ResultAsync(TypedResults.Ok(result.Value));
            return;
        }

        // If not the requestor, they must have support:manage permission (checked at handler level,
        // but for self-service endpoint we forbid non-requestor non-admin access)
        // The policy gate ensures support:request permission, but we also need to verify
        // the caller can actually access other people's requests (which is admin-only).
        // Since this endpoint is gated by support:request (not support:manage), non-requestors
        // are forbidden to maintain self-service semantics.
        await Send.ResultAsync(TypedResults.Forbid());
    }
}

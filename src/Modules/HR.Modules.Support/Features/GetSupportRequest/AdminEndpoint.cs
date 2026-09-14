using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.GetSupportRequest;

// Ticket 17: platform-support surface for HR.Admin.Web — see ListSupportRequests/AdminEndpoint.cs
// remarks. Reuses GetSupportRequestHandler unchanged, so the request must still belong to
// {companyId} (the handler filters by both Id and CompanyId), even though "platform:admin" allows
// the caller to reach any company's route.
internal sealed class AdminEndpoint(GetSupportRequestHandler handler)
    : Endpoint<GetSupportRequestRequest, GetSupportRequestResponse>
{
    public override void Configure()
    {
        Get("/api/admin/companies/{companyId:guid}/support/requests/{id:guid}");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(GetSupportRequestRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status404NotFound));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}

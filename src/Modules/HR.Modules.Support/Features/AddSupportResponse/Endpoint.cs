using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.AddSupportResponse;

internal sealed class Endpoint(AddSupportResponseHandler handler, IAuthorizationService authorizationService)
    : Endpoint<AddSupportResponseRequest, AddSupportResponseResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/support/requests/{id:guid}/responses");
        Policies("support:manage");
        AllowFileUploads();
    }

    public override async Task HandleAsync(AddSupportResponseRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // IsStaffResponse is derived from whether the caller holds the "support:manage" policy —
        // never trust a client-supplied flag for this.
        var authResult = await authorizationService.AuthorizeAsync(User, "support:manage");
        var isStaffResponse = authResult.Succeeded;

        var result = await handler.HandleAsync(request, userId, isStaffResponse, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status404NotFound));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/support/requests/{request.Id}/responses/{result.Value!.Id}", result.Value));
    }
}

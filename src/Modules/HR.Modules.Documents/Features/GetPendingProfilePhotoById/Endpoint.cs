using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetPendingProfilePhotoById;

internal sealed class Endpoint(GetPendingProfilePhotoByIdHandler handler, IAuthorizationService authorizationService)
    : Endpoint<GetPendingProfilePhotoByIdRequest, GetPendingProfilePhotoByIdResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/profile-photo/pending/{pendingPhotoId:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetPendingProfilePhotoByIdRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out _))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Verify the caller belongs to the company in the route (applies to all callers).
        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var isManagerUpload = (await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded;

        if (!isManagerUpload)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

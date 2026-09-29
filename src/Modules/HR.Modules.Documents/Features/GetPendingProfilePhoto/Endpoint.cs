using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Documents.Features.GetPendingProfilePhoto;

internal sealed class Endpoint(GetPendingProfilePhotoHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<GetPendingProfilePhotoRequest, GetPendingProfilePhotoResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/profile-photo/pending");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetPendingProfilePhotoRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
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

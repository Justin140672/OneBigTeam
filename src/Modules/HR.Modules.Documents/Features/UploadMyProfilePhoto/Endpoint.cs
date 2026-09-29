using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadMyProfilePhoto;

internal sealed class Endpoint(UploadMyProfilePhotoHandler handler, ICurrentUser currentUser)
    : Endpoint<UploadMyProfilePhotoRequest, UploadMyProfilePhotoResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/me/profile-photo");
        Policies("role:employee");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        UploadMyProfilePhotoRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}

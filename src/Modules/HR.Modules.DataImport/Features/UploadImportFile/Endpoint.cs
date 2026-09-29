using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.DataImport.Features.UploadImportFile;

// Note: there is no dedicated "data-import:manage" permission defined in the Identity module yet.
// "employee:manage" is reused here as the closest existing fit — it already gates equivalent
// bulk/administrative HR operations across other modules (e.g. CreateEmployee, CreateAssetCategory,
// UploadEmployeeDocument's manager path). If a more granular data-import permission is introduced
// later, this endpoint should be updated to use it instead.
internal sealed class Endpoint(UploadImportFileHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<UploadImportFileRequest, UploadImportFileResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/data-import/sessions");
        Policies("employee:manage");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        UploadImportFileRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid initiatedByUserId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var isAuthorized = (await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded;
        if (!isAuthorized)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, initiatedByUserId, cancellationToken);

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

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/data-import/sessions/{result.Value.Id}",
            result.Value));
    }
}

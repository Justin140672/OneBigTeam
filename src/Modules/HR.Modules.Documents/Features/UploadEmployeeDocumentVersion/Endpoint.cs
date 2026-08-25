using FastEndpoints;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadEmployeeDocumentVersion;

// DOC-05: mirrors UploadEmployeeDocument's manager/HR-only upload gate (via "employee:manage")
// plus DocumentResourceAuthorizer's self/manager-hierarchy/HR-administrator scope check — a new
// version is a document-management action, so it is held to the same authorization bar as the
// original upload, not the narrower rule that gates read-only access.
internal sealed class Endpoint(
    UploadEmployeeDocumentVersionHandler handler,
    ICurrentUser currentUser,
    DocumentResourceAuthorizer authorizer) : Endpoint<UploadEmployeeDocumentVersionRequest, UploadEmployeeDocumentVersionResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/{employeeDocumentId:guid}/versions");
        Policies("role:employee");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        UploadEmployeeDocumentVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid uploadedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // Only an HR administrator or a manager in the target employee's reporting hierarchy (or
        // the employee themselves, in line with CanAccessEmployeeDocumentsAsync's existing scope)
        // may upload a replacement version.
        if (!await authorizer.CanAccessEmployeeDocumentsAsync(
                request.CompanyId, uploadedBy, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, uploadedBy, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/employees/{result.Value.EmployeeId}/documents/{result.Value.EmployeeDocumentId}",
            result.Value));
    }
}

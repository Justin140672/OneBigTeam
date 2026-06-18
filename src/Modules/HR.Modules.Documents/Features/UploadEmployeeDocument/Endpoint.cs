using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadEmployeeDocument;

internal sealed class Endpoint(UploadEmployeeDocumentHandler handler, IAuthorizationService authorizationService)
    : Endpoint<UploadEmployeeDocumentRequest, UploadEmployeeDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents");
        Policies("authenticated");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        UploadEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uploadedBy))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Verify the caller belongs to the company in the route (applies to all callers).
        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await SendResultAsync(TypedResults.Forbid());
            return;
        }

        var authResult    = await authorizationService.AuthorizeAsync(User, "employee:manage");
        var isManagerUpload = authResult.Succeeded;

        // Non-managers may only upload to their own employee record.
        if (!isManagerUpload && uploadedBy != request.EmployeeId)
        {
            await SendResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, uploadedBy, isManagerUpload, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(error));
                return;
            }

            await SendResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        HttpContext.Response.Headers.Location =
            $"/api/companies/{result.Value!.CompanyId}/employees/{result.Value.EmployeeId}/documents/{result.Value.EmployeeDocumentId}";

        await SendAsync(result.Value, StatusCodes.Status201Created, cancellationToken);
    }
}

using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadEmployeeDocument;

internal sealed class Endpoint(UploadEmployeeDocumentHandler handler)
    : Endpoint<UploadEmployeeDocumentRequest, UploadEmployeeDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents");
        Policies("employee:manage");
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

        var result = await handler.HandleAsync(request, uploadedBy, cancellationToken);

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

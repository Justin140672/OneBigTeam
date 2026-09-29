using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocument;

internal sealed class Endpoint(UploadSharedCompanyDocumentHandler handler, ICurrentUser currentUser)
    : Endpoint<UploadSharedCompanyDocumentRequest, UploadSharedCompanyDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/shared-documents");
        Policies("shared-document:manage");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        UploadSharedCompanyDocumentRequest request,
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

        var result = await handler.HandleAsync(request, uploadedBy, cancellationToken);

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
            $"/api/companies/{result.Value!.CompanyId}/shared-documents/{result.Value.Id}",
            result.Value));
    }
}

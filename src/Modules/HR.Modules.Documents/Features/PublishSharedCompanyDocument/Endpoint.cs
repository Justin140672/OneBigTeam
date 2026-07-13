using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.PublishSharedCompanyDocument;

internal sealed class Endpoint(PublishSharedCompanyDocumentHandler handler)
    : Endpoint<PublishSharedCompanyDocumentRequest, PublishSharedCompanyDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/publish");
        Policies("shared-document:publish");
    }

    public override async Task HandleAsync(
        PublishSharedCompanyDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var publishedBy))
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

        var result = await handler.HandleAsync(request, publishedBy, cancellationToken);

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

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

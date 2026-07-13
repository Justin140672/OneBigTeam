using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed class Endpoint(AcknowledgeSharedCompanyDocumentHandler handler)
    : Endpoint<AcknowledgeSharedCompanyDocumentRequest, AcknowledgeSharedCompanyDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/acknowledge");
        Policies("shared-document:view-published");
    }

    public override async Task HandleAsync(
        AcknowledgeSharedCompanyDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var callerEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, callerEmployeeId, cancellationToken);

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

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

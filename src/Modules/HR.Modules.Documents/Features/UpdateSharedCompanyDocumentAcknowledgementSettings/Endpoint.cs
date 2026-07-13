using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

internal sealed class Endpoint(UpdateSharedCompanyDocumentAcknowledgementSettingsHandler handler)
    : Endpoint<UpdateSharedCompanyDocumentAcknowledgementSettingsRequest, UpdateSharedCompanyDocumentAcknowledgementSettingsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/acknowledgement-settings");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        UpdateSharedCompanyDocumentAcknowledgementSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var updatedBy))
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

        var result = await handler.HandleAsync(request, updatedBy, cancellationToken);

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

        await Send.OkAsync(result.Value!, cancellation: cancellationToken);
    }
}

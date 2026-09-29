using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;

internal sealed class Endpoint(UpdateSharedCompanyDocumentAudienceHandler handler, ICurrentUser currentUser)
    : Endpoint<UpdateSharedCompanyDocumentAudienceRequest, UpdateSharedCompanyDocumentAudienceResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/audience");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        UpdateSharedCompanyDocumentAudienceRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid updatedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, updatedBy, cancellationToken);

        if (result.IsFailure)
        {
            // Include `code` so the client can distinguish a stale-save 409 (code "concurrency")
            // from a plain conflict and raise the shared <SaveConflictBanner> — matches the
            // { error, code } envelope ProblemResults.FromError emits for every other Ticket 2 endpoint.
            var error = new { error = result.Error.Message, code = result.Error.Code };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            if (result.Error.Code == "concurrency")
            {
                await Send.ResultAsync(TypedResults.Conflict(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.OkAsync(result.Value!, cancellation: cancellationToken);
    }
}

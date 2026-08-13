using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed class Endpoint(AcknowledgeSharedCompanyDocumentHandler handler, ICurrentUser currentUser)
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
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid callerEmployeeId)
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

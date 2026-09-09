using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;

internal sealed class Endpoint(UpdateSharedCompanyDocumentMetadataHandler handler, ICurrentUser currentUser)
    : Endpoint<UpdateSharedCompanyDocumentMetadataRequest, UpdateSharedCompanyDocumentMetadataResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}");
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        UpdateSharedCompanyDocumentMetadataRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid updatedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware).
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

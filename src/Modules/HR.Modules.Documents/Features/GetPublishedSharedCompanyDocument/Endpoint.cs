using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetPublishedSharedCompanyDocument;

internal sealed class Endpoint(GetPublishedSharedCompanyDocumentHandler handler, ICurrentUser currentUser)
    : Endpoint<GetPublishedSharedCompanyDocumentRequest, GetPublishedSharedCompanyDocumentResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/published/{documentId:guid}");
        Policies("shared-document:view-published");
    }

    public override async Task HandleAsync(
        GetPublishedSharedCompanyDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // Self-scoped by the caller's own id (== Employee.Id, same convention as
        // GetMyEmployee/GetMyOnboardingStatus) — needed to resolve their department/location
        // for the audience check and their own acknowledgement state.
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
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DownloadSharedCompanyDocumentVersion;

internal sealed class Endpoint(DownloadSharedCompanyDocumentVersionHandler handler, ICurrentUser currentUser)
    : Endpoint<DownloadSharedCompanyDocumentVersionRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/versions/{versionNumber:int}/download");
        // HR-only — unlike DownloadSharedCompanyDocument, employees must never access past
        // versions, only the current published file.
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        DownloadSharedCompanyDocumentVersionRequest request,
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

        // Explicit belt-and-suspenders check that the caller belongs to the company in the
        // route, on top of the global TenantRouteAuthorizationMiddleware — same pattern as
        // DownloadSharedCompanyDocument, since this endpoint hands back a real (if time-limited)
        // download URL and deserves the same care as a mutation.
        // Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, callerEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.RedirectAsync(result.Value!.ToString(), isPermanent: false, allowRemoteRedirects: true);
    }
}

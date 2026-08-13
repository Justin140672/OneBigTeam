using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Documents.Features.DownloadSharedCompanyDocument;

internal sealed class Endpoint(DownloadSharedCompanyDocumentHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<DownloadSharedCompanyDocumentRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/{documentId:guid}/download");
        // shared-document:view-published already includes HrAdministrator, so this single gate
        // covers both audiences — the manage-vs-published-only distinction happens below.
        Policies("shared-document:view-published");
    }

    public override async Task HandleAsync(
        DownloadSharedCompanyDocumentRequest request,
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
        // UploadSharedCompanyDocument, since this endpoint hands back a real (if time-limited)
        // download URL and deserves the same care as a mutation.
        // Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var canManage = (await authorizationService.AuthorizeAsync(User, "shared-document:manage")).Succeeded;

        var result = await handler.HandleAsync(request, callerEmployeeId, canManage, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.RedirectAsync(result.Value!.ToString(), isPermanent: false, allowRemoteRedirects: true);
    }
}

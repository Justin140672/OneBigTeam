using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DownloadSharedCompanyDocument;

internal sealed class Endpoint(DownloadSharedCompanyDocumentHandler handler, IAuthorizationService authorizationService)
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
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var callerEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Explicit belt-and-suspenders check that the caller belongs to the company in the
        // route, on top of the global TenantRouteAuthorizationMiddleware — same pattern as
        // UploadSharedCompanyDocument, since this endpoint hands back a real (if time-limited)
        // download URL and deserves the same care as a mutation.
        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
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

using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DownloadSharedCompanyDocumentVersion;

internal sealed class Endpoint(DownloadSharedCompanyDocumentVersionHandler handler)
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
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var callerEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Explicit belt-and-suspenders check that the caller belongs to the company in the
        // route, on top of the global TenantRouteAuthorizationMiddleware — same pattern as
        // DownloadSharedCompanyDocument, since this endpoint hands back a real (if time-limited)
        // download URL and deserves the same care as a mutation.
        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
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

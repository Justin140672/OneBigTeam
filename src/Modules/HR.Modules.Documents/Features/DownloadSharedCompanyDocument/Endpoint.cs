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

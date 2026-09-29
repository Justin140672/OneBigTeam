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
        Policies("shared-document:manage");
    }

    public override async Task HandleAsync(
        DownloadSharedCompanyDocumentVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

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

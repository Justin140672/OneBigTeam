using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DownloadEmployeeDocument;

internal sealed class Endpoint(DownloadEmployeeDocumentHandler handler)
    : Endpoint<DownloadEmployeeDocumentRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/{employeeDocumentId:guid}/download");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        DownloadEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var downloadedBy))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, downloadedBy, cancellationToken);

        if (result.IsFailure)
        {
            await SendResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await SendRedirectAsync(result.Value!.ToString(), isPermanent: false, allowRemoteRedirects: true);
    }
}

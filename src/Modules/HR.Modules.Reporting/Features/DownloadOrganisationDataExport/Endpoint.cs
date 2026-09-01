using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.DownloadOrganisationDataExport;

internal sealed class Endpoint(
    DownloadOrganisationDataExportHandler handler,
    ICurrentUser currentUser)
    : Endpoint<DownloadOrganisationDataExportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/data-exports/{exportId:guid}/download");
        Policies("role:company-administrator");
    }

    public override async Task HandleAsync(DownloadOrganisationDataExportRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, userId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        var file = result.Value!;
        await Send.BytesAsync(file.Content, fileName: file.FileName, contentType: file.ContentType, cancellation: cancellationToken);
    }
}

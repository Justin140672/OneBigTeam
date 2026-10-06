using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace HR.Modules.Reporting.Features.DownloadOrganisationDataExport;

internal sealed class Endpoint(
    DownloadOrganisationDataExportHandler handler,
    ICurrentUser currentUser)
    : Endpoint<DownloadOrganisationDataExportRequest>
{
    private const int CopyBufferSize = 81920;

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
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        var file = result.Value!;
        await using var content = file.Content;

        var response = HttpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = file.ContentType;
        response.ContentLength = file.ContentLength;
        response.Headers[HeaderNames.ContentDisposition] =
            new ContentDispositionHeaderValue("attachment") { FileName = file.FileName, FileNameStar = file.FileName }.ToString();
        response.Headers[HeaderNames.CacheControl] = "private, no-store";

        try
        {
            await response.StartAsync(cancellationToken);
            await content.CopyToAsync(response.Body, CopyBufferSize, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
    }
}

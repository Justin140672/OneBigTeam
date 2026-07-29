using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportProbationReport;

internal sealed class Endpoint(
    ExportProbationReportHandler handler,
    IAuthorizationService authorizationService) : Endpoint<ExportProbationReportRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/probation/export");
        Policies("reporting:view-probation");
    }

    public override async Task HandleAsync(
        ExportProbationReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var callerEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var callerIsHr = (await authorizationService.AuthorizeAsync(User, "reporting:view-hr")).Succeeded;

        var result = await handler.HandleAsync(request, callerIsHr, callerEmployeeId, cancellationToken);
        var file = result.Value!.File;

        await Send.BytesAsync(
            file.Content,
            fileName: file.FileName,
            contentType: file.ContentType,
            cancellation: cancellationToken);
    }
}

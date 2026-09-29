using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportWorkloadActions;

internal sealed class Endpoint(ExportWorkloadActionsHandler handler)
    : Endpoint<ExportWorkloadActionsRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/workload-actions/export");
        Policies("reporting:view-workload-actions");
    }

    public override async Task HandleAsync(
        ExportWorkloadActionsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, User, cancellationToken);
        var file = result.Value!.File;

        await Send.BytesAsync(
            file.Content,
            fileName: file.FileName,
            contentType: file.ContentType,
            cancellation: cancellationToken);
    }
}

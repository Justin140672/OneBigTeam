using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.ExportWorkloadActions;

internal sealed class Endpoint(ExportWorkloadActionsHandler handler)
    : Endpoint<ExportWorkloadActionsRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/workload-actions/export");
        // Same reporting:view-workload-actions policy as GetWorkloadActions (Manager, HrAdministrator
        // only) — the shared access gate resolved via the ReportRegistry, so authorization cannot
        // drift independently from the view endpoint or the catalogue. Per-category row-level
        // scoping still happens inside each IWorkloadActionProvider, not here. See
        // GetWorkloadActions/Endpoint.cs.
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

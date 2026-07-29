using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetAssetAssignmentReport;

internal sealed class Endpoint(GetAssetAssignmentReportHandler handler)
    : Endpoint<GetAssetAssignmentReportRequest, GetAssetAssignmentReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/asset-assignment");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetAssetAssignmentReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

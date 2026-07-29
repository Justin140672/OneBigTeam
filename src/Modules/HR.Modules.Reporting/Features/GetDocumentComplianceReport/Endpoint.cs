using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetDocumentComplianceReport;

internal sealed class Endpoint(GetDocumentComplianceReportHandler handler)
    : Endpoint<GetDocumentComplianceReportRequest, GetDocumentComplianceReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/document-compliance");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetDocumentComplianceReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

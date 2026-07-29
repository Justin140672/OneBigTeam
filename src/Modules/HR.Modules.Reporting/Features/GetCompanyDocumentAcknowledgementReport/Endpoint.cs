using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;

internal sealed class Endpoint(GetCompanyDocumentAcknowledgementReportHandler handler)
    : Endpoint<GetCompanyDocumentAcknowledgementReportRequest, GetCompanyDocumentAcknowledgementReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/document-acknowledgement");
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetCompanyDocumentAcknowledgementReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

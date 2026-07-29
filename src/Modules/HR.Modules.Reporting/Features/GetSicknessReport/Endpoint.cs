using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetSicknessReport;

internal sealed class Endpoint(GetSicknessReportHandler handler)
    : Endpoint<GetSicknessReportRequest, GetSicknessReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/sickness");
        // HR Administrator only — sickness is sensitive health data, deliberately not extended to
        // Managers (unlike the Leave Summary/Probation reports).
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetSicknessReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetVacancyPerformanceReport;

internal sealed class Endpoint(GetVacancyPerformanceReportHandler handler)
    : Endpoint<GetVacancyPerformanceReportRequest, GetVacancyPerformanceReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/vacancy-performance");
        Policies("reporting:view-recruitment");
    }

    public override async Task HandleAsync(
        GetVacancyPerformanceReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

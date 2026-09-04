using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetEqualityDiversityReport;

/// <summary>
/// Anonymous aggregate equality &amp; diversity statistics for HR analytics. Gated on the
/// dedicated <c>reporting:view-equality</c> permission (HR Administrator) — never
/// <c>role:employee</c>. Returns counts/percentages only; no individual answers.
/// </summary>
internal sealed class Endpoint(GetEqualityDiversityReportHandler handler)
    : Endpoint<GetEqualityDiversityReportRequest, GetEqualityDiversityReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/equality-diversity");
        Policies("reporting:view-equality");
    }

    public override async Task HandleAsync(GetEqualityDiversityReportRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}

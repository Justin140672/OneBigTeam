using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetEqualityDiversityReport;

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

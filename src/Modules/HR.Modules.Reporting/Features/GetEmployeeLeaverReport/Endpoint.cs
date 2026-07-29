using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetEmployeeLeaverReport;

internal sealed class Endpoint(GetEmployeeLeaverReportHandler handler)
    : Endpoint<GetEmployeeLeaverReportRequest, GetEmployeeLeaverReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/employee-leavers");
        // Sensitive HR data (leaving details) — HR Administrator scope only, per product decision.
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetEmployeeLeaverReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

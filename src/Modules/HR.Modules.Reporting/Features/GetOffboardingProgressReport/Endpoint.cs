using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetOffboardingProgressReport;

internal sealed class Endpoint(GetOffboardingProgressReportHandler handler)
    : Endpoint<GetOffboardingProgressReportRequest, GetOffboardingProgressReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/offboarding-progress");
        // Sensitive HR data (access/asset return status) — HR Administrator scope only, per product
        // decision. Mirrors GetEmployeeLeaverReport/Endpoint.cs.
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetOffboardingProgressReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetEmployeeStarterReport;

internal sealed class Endpoint(GetEmployeeStarterReportHandler handler)
    : Endpoint<GetEmployeeStarterReportRequest, GetEmployeeStarterReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/employee-starters");
        // HR Administrators and Recruiters both need this report (Recruiters to track their own
        // placements per OBT-704) — reporting:view-employee-starter is an OR-of-roles policy
        // (HrAdministrator OR Recruiter), not an AND of reporting:view-hr/-recruitment.
        Policies("reporting:view-employee-starter");
    }

    public override async Task HandleAsync(
        GetEmployeeStarterReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

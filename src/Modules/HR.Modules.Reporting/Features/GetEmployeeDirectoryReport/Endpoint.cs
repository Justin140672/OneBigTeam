using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;

internal sealed class Endpoint(GetEmployeeDirectoryReportHandler handler)
    : Endpoint<GetEmployeeDirectoryReportRequest, GetEmployeeDirectoryReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/employee-directory");
        // This report returns every employee company-wide including email address and manager
        // assignment — HR-territory PII, not something a Manager or Recruiter should see
        // company-wide just because they have baseline reporting access. Same category
        // precedent as reporting:view-hr / reporting:view-recruitment on GetReportCatalog.
        Policies("reporting:view-hr");
    }

    public override async Task HandleAsync(
        GetEmployeeDirectoryReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

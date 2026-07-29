using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetProbationReport;

internal sealed class Endpoint(
    GetProbationReportHandler handler,
    IAuthorizationService authorizationService) : Endpoint<GetProbationReportRequest, GetProbationReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/probation");
        // Manager OR HrAdministrator baseline access — the handler enforces row-level scoping down
        // to direct reports for non-HR callers (see Handler.cs), mirroring
        // GetLeaveSummaryReport/Endpoint.cs exactly.
        Policies("reporting:view-probation");
    }

    public override async Task HandleAsync(
        GetProbationReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var callerEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var callerIsHr = (await authorizationService.AuthorizeAsync(User, "reporting:view-hr")).Succeeded;

        var result = await handler.HandleAsync(request, callerIsHr, callerEmployeeId, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}

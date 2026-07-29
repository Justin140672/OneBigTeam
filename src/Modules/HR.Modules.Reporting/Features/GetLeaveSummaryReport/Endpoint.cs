using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Reporting.Features.GetLeaveSummaryReport;

internal sealed class Endpoint(
    GetLeaveSummaryReportHandler handler,
    IAuthorizationService authorizationService) : Endpoint<GetLeaveSummaryReportRequest, GetLeaveSummaryReportResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/reporting/leave-summary");
        // Manager OR HrAdministrator baseline access — the handler enforces row-level scoping down
        // to direct reports for non-HR callers (see Handler.cs), so the policy alone is not the
        // only gate protecting company-wide data.
        Policies("reporting:view-leave-summary");
    }

    public override async Task HandleAsync(
        GetLeaveSummaryReportRequest request,
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

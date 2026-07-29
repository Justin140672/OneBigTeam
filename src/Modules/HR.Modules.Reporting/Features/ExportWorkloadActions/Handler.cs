using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetWorkloadActions;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.ExportWorkloadActions;

/// <summary>
/// Exports the Workload &amp; HR Actions Report (OBT-721), respecting the same filters and
/// per-provider row-level scoping as GetWorkloadActionsHandler — this handler delegates to it
/// rather than re-implementing aggregation/filtering, matching every other Export*Report handler's
/// "delegate to the paired Get* handler" pattern (see ExportAssetAssignmentReport/Handler.cs).
/// </summary>
internal sealed class ExportWorkloadActionsHandler(
    GetWorkloadActionsHandler getHandler,
    IReportExporter reportExporter)
{
    private static readonly string[] ColumnHeaders =
    [
        "Employee", "Department", "Action Type", "Category", "Due Date", "Assigned To", "Status", "Urgency",
    ];

    public async Task<Result<ExportWorkloadActionsResponse>> HandleAsync(
        ExportWorkloadActionsRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var getResult = await getHandler.HandleAsync(
            new GetWorkloadActionsRequest(
                request.CompanyId,
                request.ActionType,
                request.Department,
                request.Urgency,
                request.Status,
                request.EmployeeId,
                request.DueDateStart,
                request.DueDateEnd,
                request.GroupBy,
                request.ManagerId,
                request.LocationId,
                request.RecruitmentUser),
            caller,
            cancellationToken);

        if (getResult.IsFailure)
            return Result.Failure<ExportWorkloadActionsResponse>(getResult.Error);

        var rows = getResult.Value!.Items
            .Select(item => (IReadOnlyList<string?>)new List<string?>
            {
                item.EmployeeName,
                item.Department,
                item.ActionType,
                item.ActionCategory,
                item.DueDate?.ToString("yyyy-MM-dd"),
                item.AssignedTo,
                item.Status,
                item.Urgency,
            })
            .ToList();

        var exportData = new ReportExportData("Workload & HR Actions Report", ColumnHeaders, rows);
        var file = reportExporter.Export(request.Format, exportData);

        return Result.Success(new ExportWorkloadActionsResponse(file));
    }
}

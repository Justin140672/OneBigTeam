using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Documents.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for employee documents expiring soon. HR-only,
/// same source as <see cref="MissingRequiredEmployeeDocumentsWorkloadActionProvider"/>
/// (IDocumentComplianceReportReader). That reader only exposes an ExpiringSoonCount per employee
/// (no per-document names/expiry dates, unlike MissingDocumentTypeNames) — a single summary
/// WorkloadAction per affected employee is surfaced rather than one per document, documented here
/// as an interpretation given the reader's current shape (same "document rather than block"
/// approach used by VacanciesAwaitingActionWorkloadActionProvider for its own reader gaps).
/// </summary>
internal sealed class EmployeeDocumentsExpiringSoonWorkloadActionProvider(
    IDocumentComplianceReportReader documentComplianceReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Employee Documents Expiring Soon";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var items = await documentComplianceReportReader.GetDocumentComplianceReportAsync(
            companyId, positionProfileId: null, cancellationToken);

        var expiring = items.Where(i => i.ExpiringSoonCount > 0).ToList();
        if (expiring.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, expiring.Select(i => i.EmployeeId), cancellationToken);

        return expiring.Select(item =>
        {
            departments.TryGetValue(item.EmployeeId, out var dept);
            var plural = item.ExpiringSoonCount == 1 ? "document" : "documents";

            return new WorkloadAction(
                EmployeeId: item.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: $"{item.ExpiringSoonCount} {plural} expiring soon",
                ActionCategory: ActionCategory,
                DueDate: null,
                AssignedTo: null,
                Status: "Expiring Soon",
                DeepLinkUrl: $"/companies/{companyId}/employees/{item.EmployeeId}/documents");
        }).ToList();
    }
}

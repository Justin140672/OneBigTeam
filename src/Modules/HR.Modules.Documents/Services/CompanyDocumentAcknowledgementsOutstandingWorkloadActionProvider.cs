using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Documents.Services;

internal sealed class CompanyDocumentAcknowledgementsOutstandingWorkloadActionProvider(
    ICompanyDocumentAcknowledgementReportReader acknowledgementReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Company Document Acknowledgements Outstanding";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        if (requestedScope != WorkloadScope.Hr)
            return [];

        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var items = await acknowledgementReportReader.GetAcknowledgementReportAsync(companyId, cancellationToken);

        var outstanding = items.Where(i => !i.Acknowledged).ToList();
        if (outstanding.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, outstanding.Select(i => i.EmployeeId), cancellationToken);

        return outstanding.Select(item =>
        {
            departments.TryGetValue(item.EmployeeId, out var dept);

            return new WorkloadAction(
                EmployeeId: item.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: $"Acknowledge \"{item.DocumentTitle}\"",
                ActionCategory: ActionCategory,
                DueDate: null,
                AssignedTo: null,
                Status: "Not Acknowledged",
                DeepLinkUrl: $"/companies/{companyId}/shared-documents/{item.SharedCompanyDocumentId}/acknowledgement-progress");
        }).ToList();
    }
}

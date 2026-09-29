using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Documents.Services;

internal sealed class MissingRequiredEmployeeDocumentsWorkloadActionProvider(
    IDocumentComplianceReportReader documentComplianceReportReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Missing Required Employee Documents";

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

        var items = await documentComplianceReportReader.GetDocumentComplianceReportAsync(
            companyId, positionProfileId: null, cancellationToken);

        var withMissing = items.Where(i => i.MissingCount > 0).ToList();
        if (withMissing.Count == 0)
            return [];

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(
            companyId, withMissing.Select(i => i.EmployeeId), cancellationToken);

        var actions = new List<WorkloadAction>();
        foreach (var item in withMissing)
        {
            departments.TryGetValue(item.EmployeeId, out var dept);

            foreach (var docTypeName in item.MissingDocumentTypeNames)
            {
                actions.Add(new WorkloadAction(
                    EmployeeId: item.EmployeeId,
                    EmployeeName: dept?.EmployeeName ?? item.EmployeeId.ToString(),
                    Department: dept?.DepartmentName,
                    ActionType: $"Provide {docTypeName}",
                    ActionCategory: ActionCategory,
                    DueDate: null,
                    AssignedTo: null,
                    Status: "Missing",
                    DeepLinkUrl: $"/companies/{companyId}/employees/{item.EmployeeId}?tab=documents"));
            }
        }

        return actions;
    }
}

using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for outstanding sickness administration.
/// HR-only, consistent with sickness being HR-only elsewhere in this epic (GetSicknessReport,
/// GetOverdueReturnToWorkReviews, GetMissingFitNotes are all reporting:view-hr-gated). Combines two
/// distinct outstanding-action shapes under one category: pending/overdue Return to Work reviews,
/// and open requests for fit note evidence — both represent an HR admin task blocking a sickness
/// case from being closed out.
/// </summary>
internal sealed class SicknessPendingActionsWorkloadActionProvider(
    SicknessDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Pending Sickness Actions";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
        if (!callerIsHr)
            return [];

        var reviews = await dbContext.ReturnToWorkReviews
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && (r.Status == ReturnToWorkReviewStatus.Pending || r.Status == ReturnToWorkReviewStatus.Overdue))
            .Select(r => new { r.Id, r.EmployeeId, r.SicknessRecordId, r.DueDate, r.Status })
            .ToListAsync(cancellationToken);

        var evidenceRequests = await (
            from e in dbContext.SicknessEvidenceRequests.AsNoTracking()
            join r in dbContext.SicknessRecords.AsNoTracking() on e.SicknessRecordId equals r.Id
            where e.CompanyId == companyId
                && (e.Status == SicknessEvidenceRequestStatus.Pending || e.Status == SicknessEvidenceRequestStatus.Overdue)
            select new { e.Id, r.EmployeeId, e.SicknessRecordId, e.DueDate, e.Status })
            .ToListAsync(cancellationToken);

        if (reviews.Count == 0 && evidenceRequests.Count == 0)
            return [];

        var employeeIds = reviews.Select(r => r.EmployeeId)
            .Concat(evidenceRequests.Select(e => e.EmployeeId))
            .Distinct();

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, employeeIds, cancellationToken);

        var actions = new List<WorkloadAction>();

        foreach (var r in reviews)
        {
            departments.TryGetValue(r.EmployeeId, out var dept);
            actions.Add(new WorkloadAction(
                EmployeeId: r.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? r.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Complete Return to Work Review",
                ActionCategory: ActionCategory,
                DueDate: r.DueDate,
                AssignedTo: null,
                Status: r.Status.ToString(),
                DeepLinkUrl: $"/companies/{companyId}/employees/{r.EmployeeId}/view"));
        }

        foreach (var e in evidenceRequests)
        {
            departments.TryGetValue(e.EmployeeId, out var dept);
            actions.Add(new WorkloadAction(
                EmployeeId: e.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? e.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Follow Up Sickness Evidence Request",
                ActionCategory: ActionCategory,
                DueDate: e.DueDate,
                AssignedTo: null,
                Status: e.Status.ToString(),
                DeepLinkUrl: $"/companies/{companyId}/employees/{e.EmployeeId}/view"));
        }

        return actions;
    }
}

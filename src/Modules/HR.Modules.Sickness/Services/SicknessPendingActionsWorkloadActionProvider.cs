using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Services;

internal sealed class SicknessPendingActionsWorkloadActionProvider(
    SicknessDbContext dbContext,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    IOpenTaskBySourceEntityReader taskReader,
    IDirectReportsReader directReportsReader,
    HR.SharedKernel.ICurrentUser currentUser) : IWorkloadActionProvider
{
    public string ActionCategory => "Pending Sickness Actions";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? managerTeamIds = null;

        if (requestedScope == WorkloadScope.Hr)
        {
            var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
            if (!callerIsHr)
                return [];
        }
        else
        {
            if (currentUser.UserId is not { } callerEmployeeId)
                return [];

            var teamIds = await directReportsReader.GetAllDescendantIdsAsync(
                companyId, callerEmployeeId, cancellationToken);

            if (teamIds.Count == 0)
                return [];

            managerTeamIds = teamIds;
        }

        var reviewsQuery = dbContext.ReturnToWorkReviews
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && (r.Status == ReturnToWorkReviewStatus.Pending || r.Status == ReturnToWorkReviewStatus.Overdue));

        if (managerTeamIds is not null)
            reviewsQuery = reviewsQuery.Where(r => managerTeamIds.Contains(r.EmployeeId));

        var reviews = await reviewsQuery
            .Select(r => new { r.Id, r.EmployeeId, r.SicknessRecordId, r.DueDate, r.Status })
            .ToListAsync(cancellationToken);

        var evidenceRequests = managerTeamIds is null
            ? await (
                from e in dbContext.SicknessEvidenceRequests.AsNoTracking()
                join r in dbContext.SicknessRecords.AsNoTracking() on e.SicknessRecordId equals r.Id
                where e.CompanyId == companyId
                    && (e.Status == SicknessEvidenceRequestStatus.Pending || e.Status == SicknessEvidenceRequestStatus.Overdue)
                select new { e.Id, r.EmployeeId, e.SicknessRecordId, e.DueDate, e.Status })
                .ToListAsync(cancellationToken)
            : [];

        if (reviews.Count == 0 && evidenceRequests.Count == 0)
            return [];

        var employeeIds = reviews.Select(r => r.EmployeeId)
            .Concat(evidenceRequests.Select(e => e.EmployeeId))
            .Distinct();

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, employeeIds, cancellationToken);

        var reviewTaskIds = await taskReader.GetOpenTaskIdsAsync(
            companyId, reviews.Select(r => r.Id), cancellationToken, TaskActionType.Review);
        var evidenceTaskIds = await taskReader.GetOpenTaskIdsAsync(
            companyId, evidenceRequests.Select(e => e.Id), cancellationToken, TaskActionType.Upload);

        var reviewsOwnedByViewer = managerTeamIds is not null;

        var actions = new List<WorkloadAction>();

        foreach (var r in reviews)
        {
            departments.TryGetValue(r.EmployeeId, out var dept);
            var taskId = reviewTaskIds.TryGetValue(r.Id, out var tid) ? tid : (Guid?)null;
            actions.Add(new WorkloadAction(
                EmployeeId: r.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? r.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Complete Return to Work Review",
                ActionCategory: ActionCategory,
                DueDate: r.DueDate,
                AssignedTo: null,
                Status: r.Status.ToString(),
                DeepLinkUrl: "",
                TaskId: taskId,
                IsOwnerActionable: reviewsOwnedByViewer,
                OwnerLabel: reviewsOwnedByViewer ? null : "Owned by the employee's manager"));
        }

        foreach (var e in evidenceRequests)
        {
            departments.TryGetValue(e.EmployeeId, out var dept);
            var taskId = evidenceTaskIds.TryGetValue(e.Id, out var tid) ? tid : (Guid?)null;
            actions.Add(new WorkloadAction(
                EmployeeId: e.EmployeeId,
                EmployeeName: dept?.EmployeeName ?? e.EmployeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Follow Up Sickness Evidence Request",
                ActionCategory: ActionCategory,
                DueDate: e.DueDate,
                AssignedTo: null,
                Status: e.Status.ToString(),
                DeepLinkUrl: "",
                TaskId: taskId,
                IsOwnerActionable: false,
                OwnerLabel: "Owned by the employee"));
        }

        return actions;
    }
}

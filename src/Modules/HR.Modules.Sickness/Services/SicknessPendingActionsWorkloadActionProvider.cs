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

        var evidenceRows = managerTeamIds is null
            ? await dbContext.SicknessEvidenceRequests
                .AsNoTracking()
                .Where(e => e.CompanyId == companyId
                         && (e.Status == SicknessEvidenceRequestStatus.Pending || e.Status == SicknessEvidenceRequestStatus.Overdue))
                .Select(e => new { e.Id, e.SicknessRecordId, e.DueDate, e.Status })
                .ToListAsync(cancellationToken)
            : [];

        if (reviews.Count == 0 && evidenceRows.Count == 0)
            return [];

        var referencedRecordIds = reviews.Select(r => r.SicknessRecordId)
            .Concat(evidenceRows.Select(e => e.SicknessRecordId))
            .Distinct()
            .ToList();

        var recordEmployeeIds = await dbContext.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && referencedRecordIds.Contains(r.Id))
            .Select(r => new { r.Id, r.EmployeeId })
            .ToDictionaryAsync(r => r.Id, r => r.EmployeeId, cancellationToken);

        var employeeIds = reviews.Select(r => r.EmployeeId)
            .Concat(evidenceRows.Where(e => recordEmployeeIds.ContainsKey(e.SicknessRecordId))
                .Select(e => recordEmployeeIds[e.SicknessRecordId]))
            .Distinct()
            .ToList();

        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, employeeIds, cancellationToken);

        var reviewTaskIds = await taskReader.GetOpenTaskIdsAsync(
            companyId, reviews.Select(r => r.Id), cancellationToken, TaskActionType.Review);
        var evidenceTaskIds = await taskReader.GetOpenTaskIdsAsync(
            companyId, evidenceRows.Select(e => e.Id), cancellationToken, TaskActionType.Upload);

        var hrScope = requestedScope == WorkloadScope.Hr;
        IReadOnlyDictionary<Guid, Guid?> assigneesByTask = new Dictionary<Guid, Guid?>();
        IReadOnlyDictionary<Guid, EmployeeDepartmentInfo> assigneeNames = new Dictionary<Guid, EmployeeDepartmentInfo>();
        if (hrScope && reviewTaskIds.Count > 0)
        {
            assigneesByTask = await taskReader.GetTaskAssigneesAsync(companyId, reviewTaskIds.Values, cancellationToken);
            var assigneeIds = assigneesByTask.Values.Where(v => v is not null).Select(v => v!.Value).Distinct().ToList();
            if (assigneeIds.Count > 0)
                assigneeNames = await employeeDepartmentReader.GetDepartmentsAsync(companyId, assigneeIds, cancellationToken);
        }

        var actions = new List<WorkloadAction>();

        foreach (var r in reviews)
        {
            departments.TryGetValue(r.EmployeeId, out var dept);

            if (!recordEmployeeIds.ContainsKey(r.SicknessRecordId))
            {
                actions.Add(OrphanAction(
                    r.EmployeeId, dept?.EmployeeName, dept?.DepartmentName,
                    "Complete Return to Work Review", r.DueDate, r.Status.ToString(),
                    "Unable to load the related sickness record for this return to work review. This item may require administrator investigation."));
                continue;
            }

            var taskId = reviewTaskIds.TryGetValue(r.Id, out var tid) ? tid : (Guid?)null;

            var decision = new WorkloadOwnerDecision(WorkloadActionability.CanAct, null, null);
            if (hrScope)
            {
                Guid? assignee = taskId is { } linked && assigneesByTask.TryGetValue(linked, out var a) ? a : null;
                string? assigneeName = assignee is { } assigneeId && assigneeNames.TryGetValue(assigneeId, out var info)
                    ? info.EmployeeName
                    : null;
                decision = WorkloadOwnership.ForHrViewer(
                    assignee, currentUser.UserId, unassignedBelongsToHr: false, assigneeName,
                    "Owned by the employee's manager");
            }

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
                IsOwnerActionable: decision.IsOwnerActionable,
                OwnerLabel: decision.OwnerLabel,
                VisibilityReason: decision.VisibilityReason));
        }

        foreach (var e in evidenceRows)
        {
            if (!recordEmployeeIds.TryGetValue(e.SicknessRecordId, out var employeeId))
            {
                actions.Add(OrphanAction(
                    null, null, null, "Follow Up Sickness Evidence Request", e.DueDate, e.Status.ToString(),
                    "Unable to load the related sickness evidence request. This item may require administrator investigation."));
                continue;
            }

            departments.TryGetValue(employeeId, out var dept);
            var taskId = evidenceTaskIds.TryGetValue(e.Id, out var tid) ? tid : (Guid?)null;
            actions.Add(new WorkloadAction(
                EmployeeId: employeeId,
                EmployeeName: dept?.EmployeeName ?? employeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: "Follow Up Sickness Evidence Request",
                ActionCategory: ActionCategory,
                DueDate: e.DueDate,
                AssignedTo: null,
                Status: e.Status.ToString(),
                DeepLinkUrl: "",
                TaskId: taskId,
                IsOwnerActionable: false,
                OwnerLabel: "Owned by the employee",
                VisibilityReason: "Shown so you can monitor it. The employee is responsible for providing the evidence."));
        }

        return actions;
    }

    private WorkloadAction OrphanAction(
        Guid? employeeId,
        string? employeeName,
        string? department,
        string actionType,
        DateOnly? dueDate,
        string status,
        string reason) =>
        new(
            EmployeeId: employeeId ?? Guid.Empty,
            EmployeeName: employeeName ?? "Unknown employee",
            Department: department,
            ActionType: actionType,
            ActionCategory: ActionCategory,
            DueDate: dueDate,
            AssignedTo: null,
            Status: status,
            DeepLinkUrl: "",
            ActionabilityOverride: WorkloadActionability.Unavailable,
            VisibilityReason: reason);
}

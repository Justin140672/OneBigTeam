using System.Security.Claims;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using IClock = HR.SharedKernel.IClock;

namespace HR.Modules.Probation.Services;

internal sealed class ProbationReviewsDueWorkloadActionProvider(
    ProbationDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser,
    IOpenTaskBySourceEntityReader taskReader,
    IClock clock) : IWorkloadActionProvider
{
    public string ActionCategory => "Probation Reviews Due";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
        => await ProbationReviewWorkloadActions.GetAsync(
            dbContext, directReportsReader, employeeDepartmentReader, authorizationService, currentUser, taskReader, clock,
            companyId, caller, requestedScope, ActionCategory, overdueOnly: false, cancellationToken);
}

internal sealed class OverdueProbationReviewsWorkloadActionProvider(
    ProbationDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService,
    HR.SharedKernel.ICurrentUser currentUser,
    IOpenTaskBySourceEntityReader taskReader,
    IClock clock) : IWorkloadActionProvider
{
    public string ActionCategory => "Overdue Probation Reviews";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken)
        => await ProbationReviewWorkloadActions.GetAsync(
            dbContext, directReportsReader, employeeDepartmentReader, authorizationService, currentUser, taskReader, clock,
            companyId, caller, requestedScope, ActionCategory, overdueOnly: true, cancellationToken);
}

internal static class ProbationReviewWorkloadActions
{
    public static async Task<IReadOnlyList<WorkloadAction>> GetAsync(
        ProbationDbContext dbContext,
        IDirectReportsReader directReportsReader,
        IEmployeeDepartmentReader employeeDepartmentReader,
        IAuthorizationService authorizationService,
        HR.SharedKernel.ICurrentUser currentUser,
        IOpenTaskBySourceEntityReader taskReader,
        IClock clock,
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        string actionCategory,
        bool overdueOnly,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? employeeIds = null;
        if (requestedScope == WorkloadScope.Hr)
        {
            var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;
            if (!callerIsHr)
                return [];
        }
        else
        {
            var callerIsManager = (await authorizationService.AuthorizeAsync(caller, "reporting:view-probation")).Succeeded;
            if (!callerIsManager)
                return [];

            if (currentUser.UserId is not { } callerEmployeeId)
                return [];

            var teamIds = await directReportsReader.GetAllDescendantIdsAsync(
                companyId, callerEmployeeId, cancellationToken);

            if (teamIds.Count == 0)
                return [];

            employeeIds = teamIds;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.Date);

        var recordsQuery = dbContext.ProbationRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId);

        if (employeeIds is not null)
            recordsQuery = recordsQuery.Where(r => employeeIds.Contains(r.EmployeeId));

        var records = await recordsQuery
            .Select(r => new { r.Id, r.EmployeeId })
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
            return [];

        var recordMap = records.ToDictionary(r => r.Id, r => r.EmployeeId);

        var reviewsQuery = dbContext.ProbationReviews
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && recordMap.Keys.Contains(r.ProbationRecordId)
                     && r.Status == ProbationReviewStatus.Pending);

        reviewsQuery = overdueOnly
            ? reviewsQuery.Where(r => r.DueDate < today)
            : reviewsQuery.Where(r => r.DueDate >= today);

        var reviews = await reviewsQuery
            .Select(r => new { r.Id, r.ProbationRecordId, r.DueDate, r.ReviewType })
            .ToListAsync(cancellationToken);

        if (reviews.Count == 0)
            return [];

        var reviewEmployeeIds = reviews.Select(r => recordMap[r.ProbationRecordId]).Distinct();
        var departments = await employeeDepartmentReader.GetDepartmentsAsync(companyId, reviewEmployeeIds, cancellationToken);

        var taskIdsByReview = await taskReader.GetOpenTaskIdsAsync(
            companyId, reviews.Select(r => r.Id), cancellationToken, TaskActionType.Review);

        var hrScope = requestedScope == WorkloadScope.Hr;
        IReadOnlyDictionary<Guid, Guid?> assigneesByTask = new Dictionary<Guid, Guid?>();
        IReadOnlyDictionary<Guid, EmployeeDepartmentInfo> assigneeNames = new Dictionary<Guid, EmployeeDepartmentInfo>();
        if (hrScope && taskIdsByReview.Count > 0)
        {
            assigneesByTask = await taskReader.GetTaskAssigneesAsync(companyId, taskIdsByReview.Values, cancellationToken);
            var assigneeIds = assigneesByTask.Values.Where(v => v is not null).Select(v => v!.Value).Distinct().ToList();
            if (assigneeIds.Count > 0)
                assigneeNames = await employeeDepartmentReader.GetDepartmentsAsync(companyId, assigneeIds, cancellationToken);
        }

        return reviews.Select(r =>
        {
            var employeeId = recordMap[r.ProbationRecordId];
            departments.TryGetValue(employeeId, out var dept);
            var taskId = taskIdsByReview.TryGetValue(r.Id, out var tid) ? tid : (Guid?)null;

            var decision = new WorkloadOwnerDecision(WorkloadActionability.CanAct, null, null);
            if (hrScope && taskId is { } linked)
            {
                Guid? assignee = assigneesByTask.TryGetValue(linked, out var a) ? a : null;
                string? assigneeName = assignee is { } assigneeId && assigneeNames.TryGetValue(assigneeId, out var info) ? info.EmployeeName : null;
                decision = WorkloadOwnership.ForHrViewer(
                    assignee, currentUser.UserId, unassignedBelongsToHr: false, assigneeName, "Owned by the employee's manager");
            }

            return new WorkloadAction(
                EmployeeId: employeeId,
                EmployeeName: dept?.EmployeeName ?? employeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: $"Complete {r.ReviewType} Probation Review",
                ActionCategory: actionCategory,
                DueDate: r.DueDate,
                AssignedTo: null,
                Status: overdueOnly ? "Overdue" : "Due",
                DeepLinkUrl: "",
                TaskId: taskId,
                IsOwnerActionable: decision.IsOwnerActionable,
                OwnerLabel: decision.OwnerLabel,
                VisibilityReason: decision.VisibilityReason);
        }).ToList();
    }
}

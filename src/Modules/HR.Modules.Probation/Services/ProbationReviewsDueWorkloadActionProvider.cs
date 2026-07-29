using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

/// <summary>
/// OBT-721 Workload &amp; HR Actions Report provider for probation reviews that are pending and due
/// (not yet overdue). Row-scoping mirrors GetProbationReport/Handler.cs exactly: HR sees every
/// pending review company-wide, a Manager sees only reviews for their own direct reports.
/// See <see cref="OverdueProbationReviewsWorkloadActionProvider"/> for the overdue counterpart —
/// split into two categories/providers per the OBT-721 ticket rather than one, so they can be
/// filtered/grouped independently on the dashboard.
/// </summary>
internal sealed class ProbationReviewsDueWorkloadActionProvider(
    ProbationDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Probation Reviews Due";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
        => await ProbationReviewWorkloadActions.GetAsync(
            dbContext, directReportsReader, employeeDepartmentReader, authorizationService,
            companyId, caller, ActionCategory, overdueOnly: false, cancellationToken);
}

/// <summary>
/// Overdue counterpart to <see cref="ProbationReviewsDueWorkloadActionProvider"/> — same row-scoping,
/// restricted to reviews whose DueDate has already passed.
/// </summary>
internal sealed class OverdueProbationReviewsWorkloadActionProvider(
    ProbationDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeDepartmentReader employeeDepartmentReader,
    IAuthorizationService authorizationService) : IWorkloadActionProvider
{
    public string ActionCategory => "Overdue Probation Reviews";

    public async Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken)
        => await ProbationReviewWorkloadActions.GetAsync(
            dbContext, directReportsReader, employeeDepartmentReader, authorizationService,
            companyId, caller, ActionCategory, overdueOnly: true, cancellationToken);
}

/// <summary>
/// Shared query/scoping logic for the two probation review providers above — kept as a plain
/// internal static helper (not a service of its own) since it is only ever called by these two
/// providers within the same module.
/// </summary>
internal static class ProbationReviewWorkloadActions
{
    public static async Task<IReadOnlyList<WorkloadAction>> GetAsync(
        ProbationDbContext dbContext,
        IDirectReportsReader directReportsReader,
        IEmployeeDepartmentReader employeeDepartmentReader,
        IAuthorizationService authorizationService,
        Guid companyId,
        ClaimsPrincipal caller,
        string actionCategory,
        bool overdueOnly,
        CancellationToken cancellationToken)
    {
        var callerIsHr = (await authorizationService.AuthorizeAsync(caller, "reporting:view-hr")).Succeeded;

        IReadOnlyCollection<Guid>? employeeIds = null;
        if (!callerIsHr)
        {
            var callerIsManager = (await authorizationService.AuthorizeAsync(caller, "reporting:view-probation")).Succeeded;
            if (!callerIsManager)
                return [];

            if (!Guid.TryParse(caller.FindFirst("sub")?.Value, out var callerEmployeeId))
                return [];

            var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
                companyId, callerEmployeeId, cancellationToken);

            if (directReportIds.Count == 0)
                return [];

            employeeIds = directReportIds;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

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

        return reviews.Select(r =>
        {
            var employeeId = recordMap[r.ProbationRecordId];
            departments.TryGetValue(employeeId, out var dept);

            return new WorkloadAction(
                EmployeeId: employeeId,
                EmployeeName: dept?.EmployeeName ?? employeeId.ToString(),
                Department: dept?.DepartmentName,
                ActionType: $"Complete {r.ReviewType} Probation Review",
                ActionCategory: actionCategory,
                DueDate: r.DueDate,
                AssignedTo: null,
                Status: overdueOnly ? "Overdue" : "Due",
                DeepLinkUrl: $"/companies/{companyId}/employees/{employeeId}/view");
        }).ToList();
    }
}

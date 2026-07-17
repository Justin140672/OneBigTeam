using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.GetRecentLeaveRequests;

internal sealed class GetRecentLeaveRequestsHandler(
    LeaveDbContext dbContext,
    IEmployeeNameReader employeeNameReader,
    IDirectReportsReader directReportsReader,
    IOpenTaskBySourceEntityReader openTaskReader,
    IClock clock)
{
    private const int DefaultTake = 10;

    public async Task<GetRecentLeaveRequestsResponse> HandleAsync(
        GetRecentLeaveRequestsRequest request,
        Guid viewerEmployeeId,
        bool isHrAdministrator,
        CancellationToken cancellationToken)
    {
        var take = request.Take ?? DefaultTake;
        var today = DateOnly.FromDateTime(clock.UtcNow);

        var query = dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId);

        // HR administrators keep the original company-wide, all-statuses view. Everyone else
        // (managers) is scoped to their own direct reports and pending requests only — mirrors
        // the IDirectReportsReader scoping pattern used by GetTeamTasksHandler/
        // GetTeamSicknessTodayHandler. The HR/non-HR split itself is resolved server-side by
        // the endpoint (User claims + IAuthorizationService), never trusted from the client.
        if (!isHrAdministrator)
        {
            var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
                request.CompanyId, viewerEmployeeId, cancellationToken);

            if (directReportIds.Count == 0)
                return new GetRecentLeaveRequestsResponse([]);

            query = query.Where(r => directReportIds.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Pending);
        }
        else
        {
            // An approved request stops being something an admin needs to act on once its leave
            // has actually started — hide it from this point on. Requests still awaiting a
            // decision, already declined/cancelled, and approved-but-not-yet-started requests
            // (e.g. approved ahead of time for next month) are unaffected; they're still governed
            // purely by recency/take below.
            query = query.Where(r => r.Status != LeaveRequestStatus.Approved || r.StartDate > today);
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Join(
                dbContext.LeaveTypes.AsNoTracking(),
                r => r.LeaveTypeId,
                lt => lt.Id,
                (r, lt) => new
                {
                    r.Id,
                    r.EmployeeId,
                    LeaveTypeName = lt.Name,
                    Status = r.Status.ToString(),
                    r.StartDate,
                    r.EndDate,
                    r.TotalDays,
                    r.CreatedAt,
                })
            .ToListAsync(cancellationToken);

        var employeeIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, employeeIds, cancellationToken);

        var leaveRequestIds = rows.Select(r => r.Id).ToList();
        var openTaskIds = await openTaskReader.GetOpenTaskIdsAsync(request.CompanyId, leaveRequestIds, cancellationToken);

        var items = rows
            .Select(r => new RecentLeaveRequestItem(
                r.Id,
                r.EmployeeId,
                names.GetValueOrDefault(r.EmployeeId, "Unknown Employee"),
                r.LeaveTypeName,
                r.Status,
                r.StartDate,
                r.EndDate,
                r.TotalDays,
                r.CreatedAt,
                openTaskIds.TryGetValue(r.Id, out var taskId) ? taskId : (Guid?)null))
            .ToList();

        return new GetRecentLeaveRequestsResponse(items);
    }
}

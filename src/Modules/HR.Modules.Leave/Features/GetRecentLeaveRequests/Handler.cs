using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Employees.Contracts;
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

        if (!isHrAdministrator)
        {
            var teamIds = await directReportsReader.GetAllDescendantIdsAsync(
                request.CompanyId, viewerEmployeeId, cancellationToken);

            if (teamIds.Count == 0)
                return new GetRecentLeaveRequestsResponse([]);

            query = query.Where(r => teamIds.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Pending);
        }
        else
        {
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

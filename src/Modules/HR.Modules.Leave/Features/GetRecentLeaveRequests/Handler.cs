using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.GetRecentLeaveRequests;

internal sealed class GetRecentLeaveRequestsHandler(
    LeaveDbContext dbContext,
    IEmployeeNameReader employeeNameReader)
{
    private const int DefaultTake = 10;

    public async Task<GetRecentLeaveRequestsResponse> HandleAsync(
        GetRecentLeaveRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var take = request.Take ?? DefaultTake;

        var rows = await dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId)
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
                r.CreatedAt))
            .ToList();

        return new GetRecentLeaveRequestsResponse(items);
    }
}

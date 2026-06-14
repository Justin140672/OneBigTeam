using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ListLeaveRequests;

internal sealed class ListLeaveRequestsHandler(LeaveDbContext dbContext)
{
    public async Task<ListLeaveRequestsResponse> HandleAsync(
        ListLeaveRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId && r.EmployeeId == request.EmployeeId)
            .OrderByDescending(r => r.StartDate)
            .Join(
                dbContext.LeaveTypes.AsNoTracking(),
                r => r.LeaveTypeId,
                lt => lt.Id,
                (r, lt) => new LeaveRequestItem(
                    r.Id,
                    r.LeaveTypeId,
                    lt.Name,
                    r.Status.ToString(),
                    r.StartDate,
                    r.StartPart.ToString(),
                    r.EndDate,
                    r.EndPart.ToString(),
                    r.TotalDays,
                    r.Reason,
                    r.CreatedAt))
            .ToListAsync(cancellationToken);

        return new ListLeaveRequestsResponse(items);
    }
}

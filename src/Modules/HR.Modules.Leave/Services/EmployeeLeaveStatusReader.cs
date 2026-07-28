using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

internal sealed class EmployeeLeaveStatusReader(LeaveDbContext dbContext, IClock clock) : IEmployeeLeaveStatusReader
{
    public async Task<IReadOnlySet<Guid>> GetOnLeaveTodayEmployeeIdsAsync(
        Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();

        var today = DateOnly.FromDateTime(clock.UtcNowOffset().DateTime);

        var onLeaveIds = await dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && r.Status == LeaveRequestStatus.Approved
                     && r.StartDate <= today
                     && r.EndDate >= today
                     && ids.Contains(r.EmployeeId))
            .Select(r => r.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return onLeaveIds.ToHashSet();
    }
}

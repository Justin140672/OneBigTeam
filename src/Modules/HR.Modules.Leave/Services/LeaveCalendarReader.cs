using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

internal sealed class LeaveCalendarReader(LeaveDbContext dbContext) : ILeaveCalendarReader
{
    // Row cap (REP-05) — mirrors HR.Modules.Sickness.Services.SicknessReportReader.MaxRows so this
    // reader (used by both the on-screen calendar and its export) can't return an unbounded result
    // set for a company with an unusually large volume of leave requests in a single month.
    private const int MaxRows = 50_000;

    public async Task<IReadOnlyList<LeaveCalendarReportItem>> GetLeaveCalendarAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var query = dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId &&
                        r.StartDate <= monthEnd &&
                        r.EndDate >= monthStart);

        if (employeeIds is { Count: > 0 })
            query = query.Where(r => employeeIds.Contains(r.EmployeeId));

        // Deterministic ordering with an explicit tiebreaker (REP-05) — StartDate alone is not
        // unique across rows.
        var requests = await query
            .OrderBy(r => r.StartDate)
            .ThenBy(r => r.Id)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        var leaveTypeIds = requests.Select(r => r.LeaveTypeId).ToHashSet();
        var leaveTypeNames = leaveTypeIds.Count > 0
            ? await dbContext.LeaveTypes.AsNoTracking()
                .Where(t => leaveTypeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        return requests
            .Select(r => new LeaveCalendarReportItem(
                r.EmployeeId,
                r.StartDate,
                r.EndDate,
                leaveTypeNames.TryGetValue(r.LeaveTypeId, out var name) ? name : "Unknown",
                r.TotalDays,
                r.Status.ToString()))
            .ToList();
    }
}

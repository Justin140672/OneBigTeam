using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

internal sealed class LeaveSummaryReader(LeaveDbContext dbContext) : ILeaveSummaryReader
{
    public async Task<IReadOnlyList<LeaveSummaryReportRow>> GetLeaveSummaryAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        int policyYear,
        CancellationToken cancellationToken)
    {
        var balanceQuery = dbContext.LeaveBalances
            .AsNoTracking()
            .Where(b => b.CompanyId == companyId && b.PolicyYear == policyYear);

        if (employeeIds is { Count: > 0 })
            balanceQuery = balanceQuery.Where(b => employeeIds.Contains(b.EmployeeId));

        var balances = await balanceQuery.ToListAsync(cancellationToken);

        var leaveTypeIds = balances.Select(b => b.LeaveTypeId).ToHashSet();

        var leaveTypeNames = leaveTypeIds.Count > 0
            ? await dbContext.LeaveTypes.AsNoTracking()
                .Where(t => leaveTypeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        // Pending request counts, per employee + leave type, for the same policy year window
        // (approximate by request start date falling within the calendar policy year — Leave's
        // policy-year windowing itself is more nuanced (see LeaveYearCalculator) but a simple
        // calendar-year filter is sufficient for a summary report count).
        var requestQuery = dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId &&
                        r.Status == LeaveRequestStatus.Pending &&
                        r.StartDate.Year == policyYear);

        if (employeeIds is { Count: > 0 })
            requestQuery = requestQuery.Where(r => employeeIds.Contains(r.EmployeeId));

        var pendingCounts = await requestQuery
            .GroupBy(r => new { r.EmployeeId, r.LeaveTypeId })
            .Select(g => new { g.Key.EmployeeId, g.Key.LeaveTypeId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var pendingLookup = pendingCounts.ToDictionary(p => (p.EmployeeId, p.LeaveTypeId), p => p.Count);

        return balances
            .Select(b => new LeaveSummaryReportRow(
                b.EmployeeId,
                b.LeaveTypeId,
                leaveTypeNames.TryGetValue(b.LeaveTypeId, out var name) ? name : "Unknown",
                b.EntitlementDays,
                b.UsedDays,
                b.UsedDays,
                b.RemainingDays,
                pendingLookup.TryGetValue((b.EmployeeId, b.LeaveTypeId), out var count) ? count : 0))
            .ToList();
    }
}

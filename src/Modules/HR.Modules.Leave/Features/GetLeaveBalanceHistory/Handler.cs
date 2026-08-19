using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.GetLeaveBalanceHistory;

internal sealed class GetLeaveBalanceHistoryHandler(
    LeaveDbContext dbContext,
    IWorkingPatternProvider workingPatternProvider,
    IClock clock,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IEmployeeNameReader employeeNameReader)
{
    private const string LeaveTakenReason = "Leave Taken";
    private const string LeaveCancelledReason = "Leave Cancelled";
    private const string ToilAwardReason = "TOIL Award";
    private const string CarryOverReason = "Carry Over";

    public async Task<Result<GetLeaveBalanceHistoryResponse>> HandleAsync(
        GetLeaveBalanceHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var leaveType = await dbContext.LeaveTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                lt => lt.Id == request.LeaveTypeId && lt.CompanyId == request.CompanyId,
                cancellationToken);

        if (leaveType is null)
            return Result.Failure<GetLeaveBalanceHistoryResponse>(
                Error.NotFound($"Leave type '{request.LeaveTypeId}' was not found."));

        var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var leaveRequests = await dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId
                     && r.EmployeeId == request.EmployeeId
                     && r.LeaveTypeId == request.LeaveTypeId
                     && (r.Status == LeaveRequestStatus.Approved || r.Status == LeaveRequestStatus.Cancelled))
            .ToListAsync(cancellationToken);

        var balanceIds = await dbContext.LeaveBalances
            .AsNoTracking()
            .Where(b => b.CompanyId == request.CompanyId
                     && b.EmployeeId == request.EmployeeId
                     && b.LeaveTypeId == request.LeaveTypeId)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var toilTransactions = balanceIds.Count == 0
            ? []
            : await dbContext.ToilTransactions
                .AsNoTracking()
                .Where(t => t.CompanyId == request.CompanyId && balanceIds.Contains(t.LeaveBalanceId))
                .ToListAsync(cancellationToken);

        var adjustments = await dbContext.LeaveBalanceAdjustments
            .AsNoTracking()
            .Where(a => a.CompanyId == request.CompanyId
                     && a.EmployeeId == request.EmployeeId
                     && a.LeaveTypeId == request.LeaveTypeId)
            .ToListAsync(cancellationToken);

        // Raw, unsorted events with a signed hours "Change" and the actor responsible for it.
        // Sign convention: negative consumes balance (leave taken), positive adds to it
        // (cancellation reversal, TOIL award, or the adjustment's own signed amount).
        var raw = new List<(string Category, DateTimeOffset Date, decimal Change, string Reason, string Description, Guid ActorId)>();

        raw.AddRange(leaveRequests.Select(r => (
            Category: r.Status == LeaveRequestStatus.Approved ? "ApprovedLeave" : "CancelledLeave",
            Date: r.UpdatedAt,
            Change: r.Status == LeaveRequestStatus.Approved
                ? -(r.TotalDays * workingPattern.HoursPerDay)
                : r.TotalDays * workingPattern.HoursPerDay,
            Reason: r.Status == LeaveRequestStatus.Approved ? LeaveTakenReason : LeaveCancelledReason,
            Description: $"{(r.Status == LeaveRequestStatus.Approved ? "Leave approved" : "Leave cancelled")}: {r.StartDate:d MMM yyyy} - {r.EndDate:d MMM yyyy}" + (r.Reason is null ? "" : $" ({r.Reason})"),
            // Approved leave is actioned by the reviewer; cancellation has no separate actor
            // tracked on LeaveRequest (self-service action), so the employee is used.
            ActorId: r.Status == LeaveRequestStatus.Approved ? (r.ReviewedByEmployeeId ?? r.EmployeeId) : r.EmployeeId)));

        raw.AddRange(toilTransactions.Select(t => (
            Category: "ToilAward",
            Date: t.CreatedAt,
            Change: t.Days * workingPattern.HoursPerDay,
            Reason: ToilAwardReason,
            Description: "TOIL awarded" + (t.Notes is null ? "" : $": {t.Notes}"),
            ActorId: t.AwardedByEmployeeId)));

        raw.AddRange(adjustments.Where(a => a.Reason != LeaveBalanceAdjustmentReason.CarryOver).Select(a => (
            Category: "ManualAdjustment",
            Date: a.AdjustedAt,
            Change: a.AdjustmentDays * workingPattern.HoursPerDay,
            Reason: a.Reason.ToString(),
            Description: a.Reason.ToString() + (a.Comments is null ? "" : $": {a.Comments}"),
            ActorId: a.AdjustedByEmployeeId)));

        raw.AddRange(adjustments.Where(a => a.Reason == LeaveBalanceAdjustmentReason.CarryOver).Select(a => (
            Category: "CarryOver",
            Date: a.AdjustedAt,
            Change: a.AdjustmentDays * workingPattern.HoursPerDay,
            Reason: CarryOverReason,
            Description: "Carried over from previous year" + (a.Comments is null ? "" : $": {a.Comments}"),
            ActorId: a.AdjustedByEmployeeId)));

        var actorIds = raw.Select(x => x.ActorId).Distinct().ToList();
        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, actorIds, cancellationToken);

        // BalanceAfter is a running total. There is no stored "starting balance" record, so we
        // anchor to the one fact we do know for certain — the current policy year's remaining
        // balance — and derive the implied starting point by subtracting the sum of all known
        // changes from it. Walking forward through the chronological list from that starting
        // point guarantees the most recent event's BalanceAfter exactly matches the current
        // known remaining balance. This is only as accurate as the full set of events considered
        // here (e.g. it does not know about balance history predating any of these tables).
        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);
        var currentPolicyYear = LeaveYearCalculator.GetPolicyYear(clock.UtcNowOffset(), leaveSettings.LeaveYearStartMonth);

        var currentBalance = await dbContext.LeaveBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(
                b => b.CompanyId == request.CompanyId
                  && b.EmployeeId == request.EmployeeId
                  && b.LeaveTypeId == request.LeaveTypeId
                  && b.PolicyYear == currentPolicyYear,
                cancellationToken);

        var currentRemainingHours = currentBalance is null ? 0m : currentBalance.RemainingDays * workingPattern.HoursPerDay;

        var ascending = raw.OrderBy(x => x.Date).ToList();
        var totalChange = ascending.Sum(x => x.Change);
        var running = currentRemainingHours - totalChange;

        var items = new List<LeaveBalanceHistoryItem>(ascending.Count);
        foreach (var evt in ascending)
        {
            running += evt.Change;
            items.Add(new LeaveBalanceHistoryItem(
                evt.Category,
                evt.Date,
                leaveType.Name,
                evt.Change,
                evt.Reason,
                running,
                names.GetValueOrDefault(evt.ActorId, "Unknown Employee"),
                evt.Description));
        }

        var sorted = items.OrderByDescending(i => i.Date).ToList();

        return Result.Success(new GetLeaveBalanceHistoryResponse(request.EmployeeId, request.LeaveTypeId, sorted));
    }
}

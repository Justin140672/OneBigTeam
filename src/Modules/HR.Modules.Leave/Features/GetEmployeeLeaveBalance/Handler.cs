using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

internal sealed class GetEmployeeLeaveBalanceHandler
{
    private readonly LeaveDbContext _dbContext;
    private readonly IWorkingPatternProvider _workingPatternProvider;
    private readonly ICompanyLeaveSettingsReader _leaveSettingsReader;
    private readonly IClock _clock;

    public GetEmployeeLeaveBalanceHandler(
        LeaveDbContext dbContext,
        IWorkingPatternProvider workingPatternProvider,
        ICompanyLeaveSettingsReader leaveSettingsReader,
        IClock clock)
    {
        _dbContext = dbContext;
        _workingPatternProvider = workingPatternProvider;
        _leaveSettingsReader = leaveSettingsReader;
        _clock = clock;
    }

    public async Task<Result<GetEmployeeLeaveBalanceResponse>> HandleAsync(
        GetEmployeeLeaveBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var pendingByType = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId
                     && r.EmployeeId == request.EmployeeId
                     && r.Status == LeaveRequestStatus.Pending)
            .GroupBy(r => r.LeaveTypeId)
            .Select(g => new { LeaveTypeId = g.Key, PendingDays = g.Sum(r => r.TotalDays) })
            .ToDictionaryAsync(x => x.LeaveTypeId, x => x.PendingDays, cancellationToken);

        // All active leave types for the company are returned (left-join against any existing
        // balance row) so types with no balance for this policy year still appear in the list
        // as "n/a" rows.
        var leaveTypes = await _dbContext.LeaveTypes
            .AsNoTracking()
            .Where(lt => lt.CompanyId == request.CompanyId && lt.IsActive)
            .Select(lt => new { lt.Id, lt.Name, lt.Code, lt.HasBalance, lt.AccrualMethod, lt.Behaviour })
            .ToListAsync(cancellationToken);

        var leaveSettings = await _leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);
        var (_, policyYearEnd) = LeaveYearCalculator.GetPolicyYearBounds(request.PolicyYear, leaveSettings.LeaveYearStartMonth);
        var today = DateOnly.FromDateTime(_clock.UtcNowOffset().Date);

        var balancesByType = await _dbContext.LeaveBalances
            .AsNoTracking()
            .Where(b => b.CompanyId == request.CompanyId
                     && b.EmployeeId == request.EmployeeId
                     && b.PolicyYear == request.PolicyYear)
            .ToDictionaryAsync(b => b.LeaveTypeId, cancellationToken);

        var workingPattern = await _workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var items = leaveTypes
            .Select(lt =>
            {
                var pendingDays = pendingByType.GetValueOrDefault(lt.Id);
                var pendingHours = pendingDays * workingPattern.HoursPerDay;

                // The leave type's own HasBalance configuration is the authoritative gate: a
                // type configured as not balance-tracked (e.g. Unpaid Leave) always renders as
                // "n/a", even if a stray LeaveBalance row somehow exists for it. Only when the
                // type is balance-tracked do we then check whether a balance row exists for the
                // requested policy year.
                if (lt.HasBalance && balancesByType.TryGetValue(lt.Id, out var balance))
                {
                    // Accrued (not raw) entitlement is what's actually available - identical
                    // calculation to SubmitLeaveRequestHandler/PreviewLeaveRequestHandler so the
                    // figure shown here can never diverge from what request validation enforces
                    // (LEAVE-04).
                    var accruedDays = lt.Behaviour == LeaveTypeBehaviour.Toil
                        ? balance.EntitlementDays
                        : LeaveAccrualCalculator.CalculateAccruedDays(
                            balance.EntitlementDays,
                            lt.AccrualMethod,
                            balance.AccrualStartDate,
                            policyYearEnd,
                            today);

                    var remainingDays = accruedDays + balance.AdjustmentDays - balance.UsedDays;

                    return new LeaveBalanceItem(
                        balance.Id,
                        lt.Id,
                        lt.Name,
                        lt.Code,
                        HasBalance: true,
                        balance.EntitlementDays,
                        accruedDays,
                        balance.UsedDays,
                        balance.AdjustmentDays,
                        remainingDays,
                        pendingDays,
                        accruedDays * workingPattern.HoursPerDay,
                        remainingDays * workingPattern.HoursPerDay,
                        pendingHours);
                }

                return new LeaveBalanceItem(
                    null,
                    lt.Id,
                    lt.Name,
                    lt.Code,
                    HasBalance: false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    pendingDays,
                    null,
                    null,
                    pendingHours);
            })
            .OrderBy(x => x.LeaveTypeName)
            .ToList();

        return Result.Success(new GetEmployeeLeaveBalanceResponse(
            request.EmployeeId,
            request.PolicyYear,
            items));
    }
}

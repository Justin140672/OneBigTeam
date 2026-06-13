using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

internal sealed class GetEmployeeLeaveBalanceHandler
{
    private readonly LeaveDbContext _dbContext;

    public GetEmployeeLeaveBalanceHandler(LeaveDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetEmployeeLeaveBalanceResponse>> HandleAsync(
        GetEmployeeLeaveBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var balances = await _dbContext.LeaveBalances
            .AsNoTracking()
            .Where(b => b.CompanyId == request.CompanyId
                     && b.EmployeeId == request.EmployeeId
                     && b.PolicyYear == request.PolicyYear)
            .Join(
                _dbContext.LeaveTypes.AsNoTracking(),
                b => b.LeaveTypeId,
                lt => lt.Id,
                (b, lt) => new
                {
                    b.Id,
                    b.LeaveTypeId,
                    lt.Name,
                    lt.Code,
                    b.EntitlementDays,
                    b.UsedDays,
                    b.AdjustmentDays
                })
            .OrderBy(x => x.Name)
            .Select(x => new LeaveBalanceItem(
                x.Id,
                x.LeaveTypeId,
                x.Name,
                x.Code,
                x.EntitlementDays,
                x.UsedDays,
                x.AdjustmentDays,
                x.EntitlementDays + x.AdjustmentDays - x.UsedDays))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetEmployeeLeaveBalanceResponse(
            request.EmployeeId,
            request.PolicyYear,
            balances));
    }
}

using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.GetLeavePolicy;

internal sealed class GetLeavePolicyHandler
{
    private readonly LeaveDbContext _dbContext;

    public GetLeavePolicyHandler(LeaveDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetLeavePolicyResponse>> HandleAsync(
        GetLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _dbContext.LeavePolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == request.Id && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (policy is null)
        {
            return Result.Failure<GetLeavePolicyResponse>(
                Error.NotFound($"Leave policy with id '{request.Id}' was not found."));
        }

        return Result.Success(new GetLeavePolicyResponse(
            policy.Id,
            policy.CompanyId,
            policy.Name,
            policy.Description,
            policy.CarryOverDays,
            policy.AllowNegativeBalance,
            policy.IsActive,
            policy.CreatedAt));
    }
}

using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.UpdateLeavePolicy;

internal sealed class UpdateLeavePolicyHandler(LeaveDbContext dbContext, IClock clock)
{
    public async Task<Result<UpdateLeavePolicyResponse>> HandleAsync(
        UpdateLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(
                p => p.Id == request.PolicyId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (policy is null)
            return Result.Failure<UpdateLeavePolicyResponse>(
                Error.NotFound($"Leave policy '{request.PolicyId}' was not found."));

        var nameConflict = await dbContext.LeavePolicies
            .AnyAsync(
                p => p.CompanyId == request.CompanyId &&
                     p.Name == request.Name.Trim() &&
                     p.Id != request.PolicyId,
                cancellationToken);

        if (nameConflict)
            return Result.Failure<UpdateLeavePolicyResponse>(
                Error.Conflict($"A leave policy named '{request.Name.Trim()}' already exists in this company."));

        var now = clock.UtcNowOffset();

        policy.Update(
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.CarryOverDays,
            request.AllowNegativeBalance,
            now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateLeavePolicyResponse(
            policy.Id,
            policy.CompanyId,
            policy.Name,
            policy.Description,
            policy.CarryOverDays,
            policy.AllowNegativeBalance,
            policy.IsActive,
            policy.UpdatedAt));
    }
}

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

        if (request.IsDefault && !policy.IsDefault)
        {
            var currentDefault = await dbContext.LeavePolicies
                .SingleOrDefaultAsync(
                    p => p.CompanyId == request.CompanyId && p.IsDefault && p.Id != policy.Id,
                    cancellationToken);

            currentDefault?.UnmarkAsDefault(now);
            policy.MarkAsDefault(now);
        }
        else if (!request.IsDefault && policy.IsDefault)
        {
            return Result.Failure<UpdateLeavePolicyResponse>(
                Error.Validation("At least one Leave Policy must be marked as default — set a different policy as default instead of removing this one."));
        }

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
            policy.IsDefault,
            policy.UpdatedAt));
    }
}

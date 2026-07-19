using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.SetDefaultLeavePolicy;

internal sealed class SetDefaultLeavePolicyHandler(LeaveDbContext dbContext, IClock clock)
{
    public async Task<Result> HandleAsync(
        SetDefaultLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(
                p => p.Id == request.Id && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (policy is null)
            return Result.Failure(Error.NotFound($"Leave policy '{request.Id}' was not found."));

        if (!policy.IsActive)
            return Result.Failure(Error.Validation("Cannot set an inactive leave policy as the default."));

        if (policy.IsDefault)
            return Result.Success();

        var now = clock.UtcNowOffset();

        var currentDefault = await dbContext.LeavePolicies
            .SingleOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.IsDefault,
                cancellationToken);

        currentDefault?.UnmarkAsDefault(now);
        policy.MarkAsDefault(now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

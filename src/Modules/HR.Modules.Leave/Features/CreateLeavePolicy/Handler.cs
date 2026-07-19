using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.CreateLeavePolicy;

internal sealed class CreateLeavePolicyHandler
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;

    public CreateLeavePolicyHandler(LeaveDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreateLeavePolicyResponse>> HandleAsync(
        CreateLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var nameExists = await _dbContext.LeavePolicies
            .AnyAsync(
                p => p.CompanyId == request.CompanyId &&
                     p.Name == request.Name.Trim(),
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateLeavePolicyResponse>(
                Error.Conflict($"A leave policy named '{request.Name.Trim()}' already exists in this company."));
        }

        var now = _clock.UtcNowOffset();

        var hasAnyPolicy = await _dbContext.LeavePolicies
            .AnyAsync(p => p.CompanyId == request.CompanyId, cancellationToken);

        // A company can never have zero default policies, so the very first policy is always default.
        var isDefault = !hasAnyPolicy || request.IsDefault;

        if (hasAnyPolicy && request.IsDefault)
        {
            var currentDefault = await _dbContext.LeavePolicies
                .SingleOrDefaultAsync(p => p.CompanyId == request.CompanyId && p.IsDefault, cancellationToken);

            currentDefault?.UnmarkAsDefault(now);
        }

        var policy = LeavePolicy.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.CarryOverDays,
            request.AllowNegativeBalance,
            isDefault,
            now);

        _dbContext.LeavePolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateLeavePolicyResponse(
            policy.Id,
            policy.CompanyId,
            policy.Name,
            policy.Description,
            policy.CarryOverDays,
            policy.AllowNegativeBalance,
            policy.IsActive,
            policy.IsDefault,
            policy.CreatedAt));
    }
}

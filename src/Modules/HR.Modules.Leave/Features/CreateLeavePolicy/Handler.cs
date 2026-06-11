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

        var policy = LeavePolicy.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.CarryOverDays,
            request.AllowNegativeBalance,
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
            policy.CreatedAt));
    }
}

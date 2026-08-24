using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.CreateLeavePolicy;

internal sealed class CreateLeavePolicyHandler
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAuditEventPublisher _auditPublisher;

    public CreateLeavePolicyHandler(LeaveDbContext dbContext, IClock clock, IAuditEventPublisher auditPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditPublisher = auditPublisher;
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
            now,
            request.RequiresApproval);

        _dbContext.LeavePolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(new LeavePolicyCreatedAuditEvent(
            policy.CompanyId,
            policy.Id,
            policy.Name,
            policy.CarryOverDays,
            policy.AllowNegativeBalance,
            policy.RequiresApproval,
            policy.IsDefault,
            request.ActorEmployeeId,
            now), cancellationToken);

        return Result.Success(new CreateLeavePolicyResponse(
            policy.Id,
            policy.CompanyId,
            policy.Name,
            policy.Description,
            policy.CarryOverDays,
            policy.AllowNegativeBalance,
            policy.RequiresApproval,
            policy.IsActive,
            policy.IsDefault,
            policy.CreatedAt));
    }
}

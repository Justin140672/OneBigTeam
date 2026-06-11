using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed class AssignLeavePolicyToEmployeeHandler
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;

    public AssignLeavePolicyToEmployeeHandler(LeaveDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<AssignLeavePolicyToEmployeeResponse>> HandleAsync(
        AssignLeavePolicyToEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _dbContext.LeavePolicies
            .SingleOrDefaultAsync(
                p => p.Id == request.LeavePolicyId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (policy is null)
        {
            return Result.Failure<AssignLeavePolicyToEmployeeResponse>(
                Error.NotFound($"Leave policy with id '{request.LeavePolicyId}' was not found."));
        }

        var now = _clock.UtcNowOffset();

        var existing = await _dbContext.EmployeeLeavePolicyAssignments
            .SingleOrDefaultAsync(
                a => a.EmployeeId == request.EmployeeId && a.CompanyId == request.CompanyId,
                cancellationToken);

        EmployeeLeavePolicyAssignment assignment;

        if (existing is not null)
        {
            existing.Update(request.LeavePolicyId, request.EffectiveFrom, now);
            assignment = existing;
        }
        else
        {
            assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                request.LeavePolicyId,
                request.EffectiveFrom,
                now);

            _dbContext.EmployeeLeavePolicyAssignments.Add(assignment);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new AssignLeavePolicyToEmployeeResponse(
            assignment.Id,
            assignment.CompanyId,
            assignment.EmployeeId,
            assignment.LeavePolicyId,
            assignment.EffectiveFrom,
            assignment.CreatedAt));
    }
}

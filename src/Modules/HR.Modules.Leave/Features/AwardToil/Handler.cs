using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.AwardToil;

internal sealed class AwardToilHandler(LeaveDbContext dbContext, IClock clock, ICompanyLeaveSettingsReader leaveSettingsReader, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<AwardToilResponse>> HandleAsync(
        AwardToilRequest request,
        CancellationToken cancellationToken)
    {
        var toilLeaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.CompanyId == request.CompanyId
                   && lt.Behaviour == LeaveTypeBehaviour.Toil
                   && lt.IsActive,
                cancellationToken);

        if (toilLeaveType is null)
            return Result.Failure<AwardToilResponse>(
                Error.NotFound("No active TOIL leave type is configured for this company."));

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);
        var policyYear = LeaveYearCalculator.GetPolicyYear(request.OccurredOn, leaveSettings.LeaveYearStartMonth);
        var now = clock.UtcNowOffset();

        var balance = await dbContext.LeaveBalances
            .SingleOrDefaultAsync(
                b => b.EmployeeId == request.EmployeeId
                  && b.CompanyId == request.CompanyId
                  && b.LeaveTypeId == toilLeaveType.Id
                  && b.PolicyYear == policyYear,
                cancellationToken);

        if (balance is null)
        {
            var assignment = await dbContext.EmployeeLeavePolicyAssignments
                .FirstOrDefaultAsync(
                    a => a.CompanyId == request.CompanyId && a.EmployeeId == request.EmployeeId,
                    cancellationToken);

            if (assignment is null)
                return Result.Failure<AwardToilResponse>(
                    Error.NotFound($"Employee '{request.EmployeeId}' has no leave policy assignment."));

            balance = LeaveBalance.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                toilLeaveType.Id,
                assignment.LeavePolicyId,
                policyYear,
                0m,
                now);

            dbContext.LeaveBalances.Add(balance);
        }

        balance.Adjust(request.Days, now);

        var transaction = ToilTransaction.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            balance.Id,
            request.AwardedByEmployeeId,
            request.Days,
            request.OccurredOn,
            request.Notes,
            now);

        dbContext.ToilTransactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new LeaveBalanceAdjustedAuditEvent(
            balance.CompanyId,
            balance.EmployeeId,
            balance.LeaveTypeId,
            balance.Id,
            balance.PolicyYear,
            request.Days,
            balance.RemainingDays,
            request.AwardedByEmployeeId,
            now), cancellationToken);

        await auditPublisher.PublishAsync(new ToilAwardedAuditEvent(
            transaction.CompanyId,
            transaction.EmployeeId,
            transaction.Id,
            transaction.LeaveBalanceId,
            transaction.AwardedByEmployeeId,
            transaction.Days,
            transaction.OccurredOn,
            transaction.Notes,
            now), cancellationToken);

        return Result.Success(new AwardToilResponse(
            transaction.Id,
            transaction.CompanyId,
            transaction.EmployeeId,
            transaction.LeaveBalanceId,
            transaction.AwardedByEmployeeId,
            transaction.Days,
            balance.RemainingDays,
            transaction.OccurredOn,
            transaction.Notes,
            transaction.CreatedAt));
    }
}

using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

internal sealed class LeaveApprovalEffectsService(
    LeaveDbContext dbContext,
    INotificationWriter notificationWriter,
    IIntegrationEventPublisher publisher,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IAuditEventPublisher auditPublisher,
    ToilLedgerService toilLedgerService)
{
    public async Task<Result> ApplyBalanceEffectsAndApproveAsync(
        LeaveRequest leaveRequest,
        LeaveType? leaveType,
        Guid reviewedByEmployeeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var isToil = leaveType?.Behaviour == LeaveTypeBehaviour.Toil;

        if (isToil)
        {
            var consumeResult = await toilLedgerService.ConsumeAsync(
                leaveRequest.CompanyId,
                leaveRequest.EmployeeId,
                leaveRequest.LeaveTypeId,
                leaveRequest.TotalDays,
                leaveRequest.Id,
                reviewedByEmployeeId,
                leaveRequest.StartDate,
                leaveType!.AllowNegativeToilBalance,
                now,
                cancellationToken);

            if (consumeResult.IsFailure)
                return Result.Failure(consumeResult.Error);
        }
        else if (leaveType is null || leaveType.HasBalance)
        {
            var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(leaveRequest.CompanyId, cancellationToken);
            var policyYear = LeaveYearCalculator.GetPolicyYear(leaveRequest.StartDate, leaveSettings.LeaveYearStartMonth);

            var balance = await dbContext.LeaveBalances
                .SingleOrDefaultAsync(
                    b => b.EmployeeId == leaveRequest.EmployeeId
                      && b.CompanyId == leaveRequest.CompanyId
                      && b.LeaveTypeId == leaveRequest.LeaveTypeId
                      && b.PolicyYear == policyYear,
                    cancellationToken);

            if (balance is null)
                return Result.Failure(
                    Error.Validation(
                        $"No leave balance found for policy year {policyYear}. The request cannot be approved until a balance exists for this employee and leave type."));

            if (leaveType is not null)
            {
                var policy = await dbContext.LeavePolicies
                    .SingleOrDefaultAsync(p => p.Id == leaveRequest.LeavePolicyId, cancellationToken);

                if (policy is null || !policy.AllowNegativeBalance)
                {
                    var (_, balancePolicyYearEnd) = LeaveYearCalculator.GetPolicyYearBounds(policyYear, leaveSettings.LeaveYearStartMonth);
                    var accruedDays = LeaveAccrualCalculator.CalculateAccruedDays(
                        balance.EntitlementDays,
                        leaveType.AccrualMethod,
                        balance.AccrualStartDate,
                        balancePolicyYearEnd,
                        DateOnly.FromDateTime(now.Date));

                    var availableDays = accruedDays + balance.AdjustmentDays - balance.UsedDays;

                    if (availableDays < leaveRequest.TotalDays)
                        return Result.Failure(
                            Error.Validation(
                                $"Insufficient leave balance. This request requires {leaveRequest.TotalDays} day(s) but only {availableDays} remain available - approving would take the balance negative."));
                }
            }

            balance.RecordUsage(leaveRequest.TotalDays, now);
        }

        leaveRequest.Approve(reviewedByEmployeeId, now);
        return Result.Success();
    }

    public async Task PublishApprovalOutcomeAsync(
        LeaveRequest leaveRequest,
        Guid reviewedByEmployeeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var writeResult = await notificationWriter.WriteTemplatedAsync(
            Guid.NewGuid(), leaveRequest.CompanyId, leaveRequest.EmployeeId,
            NotificationType.LeaveApproved,
            new Dictionary<string, string>
            {
                ["StartDate"] = leaveRequest.StartDate.ToString("d MMM yyyy"),
                ["EndDate"] = leaveRequest.EndDate.ToString("d MMM yyyy"),
            },
            leaveRequest.Id,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        if (writeResult.IsFailure)
            throw new InvalidOperationException($"Failed to write LeaveApproved notification: {writeResult.Error.Message}");

        await auditPublisher.PublishAsync(new LeaveApprovedAuditEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            reviewedByEmployeeId,
            now), cancellationToken);

        await publisher.PublishAsync(new LeaveApprovedIntegrationEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            reviewedByEmployeeId,
            now), cancellationToken);
    }
}

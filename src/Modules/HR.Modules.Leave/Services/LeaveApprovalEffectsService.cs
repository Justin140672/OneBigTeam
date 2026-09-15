using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

/// <summary>
/// LEAVE-07: the balance/TOIL-ledger mutation and audit/notification side effects of approving a
/// leave request, factored out of ApproveLeaveRequestHandler so the exact same outcome (ledger
/// consumption, notification, audit event, integration event) is produced whether a request is
/// approved manually by a reviewer or automatically at submission time because its leave policy
/// does not require approval. Callers own SaveChangesAsync — ApplyBalanceEffectsAsync only stages
/// entity mutations (including leaveRequest.Approve(...)); PublishApprovalOutcomeAsync performs
/// the post-save notification/audit/integration-event fan-out.
/// </summary>
internal sealed class LeaveApprovalEffectsService(
    LeaveDbContext dbContext,
    INotificationWriter notificationWriter,
    IIntegrationEventPublisher publisher,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IAuditEventPublisher auditPublisher,
    ToilLedgerService toilLedgerService)
{
    /// <summary>
    /// Consumes the TOIL ledger or records LeaveBalance usage as appropriate for the request's
    /// leave type, then marks the request Approved. Does not call SaveChangesAsync.
    /// </summary>
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

            // A balance-tracked leave type must have a balance row for the request's policy year -
            // approving without one would silently skip deducting usage. Fail cleanly instead.
            if (balance is null)
                return Result.Failure(
                    Error.Validation(
                        $"No leave balance found for policy year {policyYear}. The request cannot be approved until a balance exists for this employee and leave type."));

            // P2: submission only checks available balance at the moment the request is created -
            // a pending request does not reserve leave. Multiple individually-valid pending requests
            // can therefore be approved sequentially without ever hitting a concurrency conflict, so
            // this check must be re-run here (using the same accrual math as submission/preview,
            // LEAVE-04) rather than relying solely on optimistic concurrency to catch the conflict.
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

    /// <summary>
    /// Notification + audit + integration event fan-out for an approved request. Must be called
    /// after SaveChangesAsync has persisted the approval.
    /// </summary>
    public async Task PublishApprovalOutcomeAsync(
        LeaveRequest leaveRequest,
        Guid reviewedByEmployeeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // NOT-03: LeaveApproved is one of the six template-backed notification types (see
        // NotificationTemplateCatalogue). The rendered in-app title/body reproduce exactly what the
        // previous inline strings produced ("Your leave request has been approved" /
        // "Your leave from {StartDate:d MMM yyyy} to {EndDate:d MMM yyyy} has been approved."),
        // since StartDate/EndDate are formatted here with the same "d MMM yyyy" format before being
        // passed as token values.
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

        // StartDate/EndDate are always supplied above, so this should never actually fail — but
        // surface it loudly rather than silently swallowing a template regression.
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

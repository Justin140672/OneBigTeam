using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class ApproveLeaveRequestHandler(LeaveDbContext dbContext, INotificationWriter notificationWriter, IClock clock, IIntegrationEventPublisher publisher, ICompanyLeaveSettingsReader leaveSettingsReader, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ApproveLeaveRequestResponse>> HandleAsync(
        ApproveLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var leaveRequest = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (leaveRequest is null)
            return Result.Failure<ApproveLeaveRequestResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (leaveRequest.Status != LeaveRequestStatus.Pending)
            return Result.Failure<ApproveLeaveRequestResponse>(
                Error.Validation($"Cannot approve a leave request with status '{leaveRequest.Status}'."));

        var now = clock.UtcNowOffset();

        var leaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(lt => lt.Id == leaveRequest.LeaveTypeId, cancellationToken);

        LeaveBalance? balance = null;
        if (leaveType?.Behaviour == LeaveTypeBehaviour.Toil)
        {
            // TOIL is not year-bound: earned in one year, taken in another. Search all years
            // and prefer the oldest balance that still has days remaining (FIFO consumption).
            var toilBalances = await dbContext.LeaveBalances
                .Where(b => b.EmployeeId == leaveRequest.EmployeeId
                         && b.CompanyId == leaveRequest.CompanyId
                         && b.LeaveTypeId == leaveRequest.LeaveTypeId)
                .OrderBy(b => b.PolicyYear)
                .ToListAsync(cancellationToken);

            balance = toilBalances.FirstOrDefault(b => b.RemainingDays > 0)
                      ?? toilBalances.FirstOrDefault();
        }
        else if (leaveType is null || leaveType.HasBalance)
        {
            var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(leaveRequest.CompanyId, cancellationToken);
            var policyYear = LeaveYearCalculator.GetPolicyYear(leaveRequest.StartDate, leaveSettings.LeaveYearStartMonth);

            balance = await dbContext.LeaveBalances
                .SingleOrDefaultAsync(
                    b => b.EmployeeId == leaveRequest.EmployeeId
                      && b.CompanyId == leaveRequest.CompanyId
                      && b.LeaveTypeId == leaveRequest.LeaveTypeId
                      && b.PolicyYear == policyYear,
                    cancellationToken);

            // A balance-tracked leave type must have a balance row for the request's policy year -
            // approving without one would silently skip deducting usage. Fail cleanly instead.
            if (balance is null)
                return Result.Failure<ApproveLeaveRequestResponse>(
                    Error.Validation(
                        $"No leave balance found for policy year {policyYear}. The request cannot be approved until a balance exists for this employee and leave type."));
        }

        leaveRequest.Approve(request.ReviewedByEmployeeId, now);
        balance?.RecordUsage(leaveRequest.TotalDays, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        await notificationWriter.WriteAsync(
            Guid.NewGuid(), leaveRequest.CompanyId, leaveRequest.EmployeeId,
            "Your leave request has been approved",
            $"Your leave from {leaveRequest.StartDate:d MMM yyyy} to {leaveRequest.EndDate:d MMM yyyy} has been approved.",
            leaveRequest.Id,
            NotificationType.LeaveApproved,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        await auditPublisher.PublishAsync(new LeaveApprovedAuditEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            request.ReviewedByEmployeeId,
            now), cancellationToken);

        await publisher.PublishAsync(new LeaveApprovedIntegrationEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            request.ReviewedByEmployeeId,
            now), cancellationToken);

        return Result.Success(new ApproveLeaveRequestResponse(
            leaveRequest.Id,
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.StartPart,
            leaveRequest.EndDate,
            leaveRequest.EndPart,
            leaveRequest.TotalDays,
            leaveRequest.Status.ToString(),
            leaveRequest.ReviewedByEmployeeId!.Value,
            leaveRequest.ReviewedAt!.Value,
            leaveRequest.UpdatedAt));
    }
}

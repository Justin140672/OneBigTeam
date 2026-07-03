using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.CancelLeaveRequest;

internal sealed class CancelLeaveRequestHandler(LeaveDbContext dbContext, IClock clock, ICompanyLeaveSettingsReader leaveSettingsReader, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CancelLeaveRequestResponse>> HandleAsync(
        CancelLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var leaveRequest = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (leaveRequest is null)
            return Result.Failure<CancelLeaveRequestResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (leaveRequest.Status is LeaveRequestStatus.Cancelled or LeaveRequestStatus.Rejected)
            return Result.Failure<CancelLeaveRequestResponse>(
                Error.Validation($"Cannot cancel a leave request with status '{leaveRequest.Status}'."));

        var now = clock.UtcNowOffset();
        var previousStatus = leaveRequest.Status.ToString();

        if (leaveRequest.Status == LeaveRequestStatus.Approved)
        {
            var leaveType = await dbContext.LeaveTypes
                .SingleOrDefaultAsync(lt => lt.Id == leaveRequest.LeaveTypeId, cancellationToken);

            LeaveBalance? balance;
            if (leaveType?.Behaviour == LeaveTypeBehaviour.Toil)
            {
                // Mirror the approval FIFO logic: reverse from the oldest balance that
                // still has used days recorded against it.
                var toilBalances = await dbContext.LeaveBalances
                    .Where(b => b.EmployeeId == leaveRequest.EmployeeId
                             && b.CompanyId == leaveRequest.CompanyId
                             && b.LeaveTypeId == leaveRequest.LeaveTypeId)
                    .OrderBy(b => b.PolicyYear)
                    .ToListAsync(cancellationToken);

                balance = toilBalances.FirstOrDefault(b => b.UsedDays > 0)
                          ?? toilBalances.FirstOrDefault();
            }
            else
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
            }

            balance?.ReverseUsage(leaveRequest.TotalDays, now);
        }

        leaveRequest.Cancel(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new LeaveCancelledAuditEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            previousStatus,
            now), cancellationToken);

        return Result.Success(new CancelLeaveRequestResponse(
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
            leaveRequest.UpdatedAt));
    }
}

using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.RejectLeaveRequest;

internal sealed class RejectLeaveRequestHandler(LeaveDbContext dbContext, IClock clock, IIntegrationEventPublisher publisher, ICompanyLeaveSettingsReader leaveSettingsReader)
{
    public async Task<Result<RejectLeaveRequestResponse>> HandleAsync(
        RejectLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var leaveRequest = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (leaveRequest is null)
            return Result.Failure<RejectLeaveRequestResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (leaveRequest.Status is not (LeaveRequestStatus.Pending or LeaveRequestStatus.Approved))
            return Result.Failure<RejectLeaveRequestResponse>(
                Error.Validation($"Cannot reject a leave request with status '{leaveRequest.Status}'."));

        var now = clock.UtcNowOffset();

        if (leaveRequest.Status == LeaveRequestStatus.Approved)
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

            balance?.ReverseUsage(leaveRequest.TotalDays, now);
        }

        leaveRequest.Reject(request.ReviewedByEmployeeId, now, request.RejectionReason);
        await dbContext.SaveChangesAsync(cancellationToken);

        await publisher.PublishAsync(new LeaveRejectedIntegrationEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            request.ReviewedByEmployeeId,
            request.RejectionReason,
            now), cancellationToken);

        return Result.Success(new RejectLeaveRequestResponse(
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
            leaveRequest.RejectionReason,
            leaveRequest.UpdatedAt));
    }
}

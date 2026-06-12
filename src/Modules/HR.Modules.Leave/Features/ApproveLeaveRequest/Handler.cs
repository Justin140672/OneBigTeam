using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class ApproveLeaveRequestHandler(LeaveDbContext dbContext, IClock clock, IIntegrationEventPublisher publisher)
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

        var balance = await dbContext.LeaveBalances
            .SingleOrDefaultAsync(
                b => b.EmployeeId == leaveRequest.EmployeeId
                  && b.CompanyId == leaveRequest.CompanyId
                  && b.LeaveTypeId == leaveRequest.LeaveTypeId
                  && b.PolicyYear == leaveRequest.StartDate.Year,
                cancellationToken);

        leaveRequest.Approve(request.ReviewedByEmployeeId, now);
        balance?.RecordUsage(leaveRequest.TotalDays, now);
        await dbContext.SaveChangesAsync(cancellationToken);

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

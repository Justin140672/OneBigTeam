using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.RejectLeaveRequest;

internal sealed class RejectLeaveRequestHandler(LeaveDbContext dbContext, IClock clock)
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

        if (leaveRequest.Status != LeaveRequestStatus.Pending)
            return Result.Failure<RejectLeaveRequestResponse>(
                Error.Validation($"Cannot reject a leave request with status '{leaveRequest.Status}'."));

        leaveRequest.Reject(request.ReviewedByEmployeeId, clock.UtcNowOffset(), request.RejectionReason);
        await dbContext.SaveChangesAsync(cancellationToken);

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

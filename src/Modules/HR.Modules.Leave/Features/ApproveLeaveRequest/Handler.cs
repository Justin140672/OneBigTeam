using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class ApproveLeaveRequestHandler(LeaveDbContext dbContext, IClock clock)
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

        leaveRequest.Approve(request.ReviewedByEmployeeId, clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

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

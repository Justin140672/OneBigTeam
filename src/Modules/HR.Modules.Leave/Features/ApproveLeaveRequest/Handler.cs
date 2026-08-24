using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ApproveLeaveRequest;

internal sealed class ApproveLeaveRequestHandler(LeaveDbContext dbContext, IClock clock, LeaveApprovalEffectsService approvalEffects)
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

        // TOIL is not year-bound: earned in one year, taken in another, and is tracked as a
        // ledger of individual awards ("buckets") rather than a single balance - see
        // ToilLedgerService for the FIFO consumption/multi-bucket-split algorithm. The mutation
        // and Approve() call both live in LeaveApprovalEffectsService (LEAVE-07) so manual
        // approval and policy-driven automatic approval share identical behaviour.
        var effectResult = await approvalEffects.ApplyBalanceEffectsAndApproveAsync(
            leaveRequest, leaveType, request.ReviewedByEmployeeId, now, cancellationToken);

        if (effectResult.IsFailure)
            return Result.Failure<ApproveLeaveRequestResponse>(effectResult.Error);

        await dbContext.SaveChangesAsync(cancellationToken);

        await approvalEffects.PublishApprovalOutcomeAsync(leaveRequest, request.ReviewedByEmployeeId, now, cancellationToken);

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

using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.DeleteLeaveRequestDraft;

// LEAVE-07: a draft never touched LeaveBalance/ToilTransaction/notifications/tasks, so deleting it
// is a plain hard delete - there is nothing to reverse and no audit trail of a "real" leave event
// to preserve (compare CancelLeaveRequestHandler, which reverses balance usage for a real request).
internal sealed class DeleteLeaveRequestDraftHandler(LeaveDbContext dbContext)
{
    public async Task<Result<DeleteLeaveRequestDraftResponse>> HandleAsync(
        DeleteLeaveRequestDraftRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (draft is null)
            return Result.Failure<DeleteLeaveRequestDraftResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (draft.Status != LeaveRequestStatus.Draft)
            return Result.Failure<DeleteLeaveRequestDraftResponse>(
                Error.Validation($"Cannot delete a leave request with status '{draft.Status}' as a draft."));

        dbContext.LeaveRequests.Remove(draft);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeleteLeaveRequestDraftResponse(request.LeaveRequestId));
    }
}

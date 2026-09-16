using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

public interface ILeaveApprovalService
{
    // Ticket 11 (P1): idempotencyKey, when supplied, is forwarded to Leave's own
    // ApproveLeaveRequestRequest/RejectLeaveRequestRequest.IdempotencyKey — the SAME
    // SaveIdempotentAsync-backed replay mechanism already used for HTTP-level idempotency (see
    // ApproveLeaveRequestHandler/RejectLeaveRequestHandler), reused here so a resumed/retried task
    // completion dispatch for the same TaskCompletionOperation converges on the original result
    // instead of failing with "Cannot approve/reject a leave request with status 'Approved'/'Rejected'"
    // on replay.
    Task<Result> ApproveAsync(Guid companyId, Guid leaveRequestId, Guid reviewedByEmployeeId, CancellationToken cancellationToken, string? idempotencyKey = null);
    Task<Result> RejectAsync(Guid companyId, Guid leaveRequestId, Guid reviewedByEmployeeId, string? reason, CancellationToken cancellationToken, string? idempotencyKey = null);
}

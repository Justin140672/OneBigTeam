using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

public interface IInterviewFeedbackService
{
    // dispatchOperationId (ticket 15, P1): stable Tasks-dispatch operation identity (Guid.Empty when
    // not applicable). When the interview already has this exact outcome recorded (e.g. a replayed
    // dispatch resuming after the primary mutation committed but before its audit event published),
    // implementations should recover the missed audit event and return success rather than the
    // generic "outcome already set" validation failure a genuinely conflicting request gets.
    Task<Result> RecordFeedbackAsync(
        Guid companyId,
        Guid interviewId,
        Guid recordedByEmployeeId,
        string outcome,
        string? notes,
        CancellationToken cancellationToken,
        Guid dispatchOperationId = default);
}

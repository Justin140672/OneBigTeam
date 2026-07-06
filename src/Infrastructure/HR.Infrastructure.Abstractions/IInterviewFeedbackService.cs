using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

public interface IInterviewFeedbackService
{
    Task<Result> RecordFeedbackAsync(
        Guid companyId,
        Guid interviewId,
        Guid recordedByEmployeeId,
        string outcome,
        string? notes,
        CancellationToken cancellationToken);
}

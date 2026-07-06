using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeInterviewFeedbackService : IInterviewFeedbackService
{
    public record Call(Guid CompanyId, Guid InterviewId, Guid RecordedByEmployeeId, string Outcome, string? Notes);

    private readonly Result _result;

    public FakeInterviewFeedbackService(Result? result = null)
    {
        _result = result ?? Result.Success();
    }

    public List<Call> Calls { get; } = [];

    public Task<Result> RecordFeedbackAsync(
        Guid companyId,
        Guid interviewId,
        Guid recordedByEmployeeId,
        string outcome,
        string? notes,
        CancellationToken cancellationToken)
    {
        Calls.Add(new Call(companyId, interviewId, recordedByEmployeeId, outcome, notes));
        return Task.FromResult(_result);
    }
}

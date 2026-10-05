using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeTaskCompletionRecovery : ITaskCompletionRecovery
{
    public TaskCompletionResetOutcome Outcome { get; set; } = TaskCompletionResetOutcome.Reset;

    public List<ResetCall> Calls { get; } = [];

    public sealed record ResetCall(Guid CompanyId, Guid OperationId, Guid OperatorUserId, string Reason);

    public Task<TaskCompletionResetResult> ResetTerminalCompletionAsync(
        Guid companyId, Guid operationId, Guid operatorUserId, string reason, CancellationToken cancellationToken)
    {
        Calls.Add(new ResetCall(companyId, operationId, operatorUserId, reason));
        return Task.FromResult(new TaskCompletionResetResult(Outcome, operationId));
    }
}

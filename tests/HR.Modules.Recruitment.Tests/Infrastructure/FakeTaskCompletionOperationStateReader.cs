using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeTaskCompletionOperationStateReader : ITaskCompletionOperationStateReader
{
    private readonly Dictionary<(Guid CompanyId, Guid OperationId), TaskCompletionOperationState> _states = [];

    public bool ThrowOnRead { get; set; }

    public void Set(Guid companyId, TaskCompletionOperationState state) => _states[(companyId, state.OperationId)] = state;

    public Task<TaskCompletionOperationState?> GetAsync(Guid companyId, Guid operationId, CancellationToken cancellationToken)
    {
        if (ThrowOnRead)
            throw new InvalidOperationException("Simulated Tasks read failure.");

        return Task.FromResult(_states.GetValueOrDefault((companyId, operationId)));
    }
}

using HR.Infrastructure.Abstractions;

namespace HR.Modules.Leave.Tests.Infrastructure;

internal sealed class FakeCurrentEmployeeReader(IReadOnlyList<Guid> currentEmployeeIds) : ICurrentEmployeeReader
{
    public FakeCurrentEmployeeReader() : this([]) { }

    public Task<IReadOnlyList<Guid>> GetCurrentEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(currentEmployeeIds);
}

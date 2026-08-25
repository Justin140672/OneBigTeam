using HR.Infrastructure.Abstractions;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeActiveLeavingProcessReader(
    IReadOnlyList<ActiveLeavingProcessItem>? items = null) : IActiveLeavingProcessReader
{
    private readonly IReadOnlyList<ActiveLeavingProcessItem> _items = items ?? [];

    public Task<IReadOnlyList<ActiveLeavingProcessItem>> GetInProgressLeavingProcessesAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(_items);
}

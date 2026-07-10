using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeOutstandingAssetAcknowledgementReader(
    IReadOnlyList<OutstandingAssetAcknowledgementItem>? items = null) : IOutstandingAssetAcknowledgementReader
{
    private readonly IReadOnlyList<OutstandingAssetAcknowledgementItem> _items = items ?? [];

    public Task<IReadOnlyList<OutstandingAssetAcknowledgementItem>> GetOutstandingAcknowledgementsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_items);
}

using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeLegalHoldStatusReader : ILegalHoldStatusReader
{
    private readonly HashSet<Guid> _heldCompanyIds;

    public FakeLegalHoldStatusReader(params Guid[] heldCompanyIds)
    {
        _heldCompanyIds = [.. heldCompanyIds];
    }

    public List<Guid> Queried { get; } = [];

    public Task<bool> IsUnderLegalHoldAsync(Guid companyId, CancellationToken cancellationToken)
    {
        Queried.Add(companyId);
        return Task.FromResult(_heldCompanyIds.Contains(companyId));
    }
}

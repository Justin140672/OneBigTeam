using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeLegalHoldStatusReader : ILegalHoldStatusReader
{
    private readonly HashSet<Guid> _heldCompanyIds;

    public FakeLegalHoldStatusReader(params Guid[] heldCompanyIds)
    {
        _heldCompanyIds = [.. heldCompanyIds];
    }

    public void PlaceHold(Guid companyId) => _heldCompanyIds.Add(companyId);

    public Task<bool> IsUnderLegalHoldAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(_heldCompanyIds.Contains(companyId));
}

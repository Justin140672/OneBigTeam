using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Tests.Infrastructure;

// Mirrors the fakes of the same name in HR.Modules.Notifications.Tests / HR.Modules.Recruitment.Tests /
// HR.Modules.Documents.Tests, which are project-local rather than shared.
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

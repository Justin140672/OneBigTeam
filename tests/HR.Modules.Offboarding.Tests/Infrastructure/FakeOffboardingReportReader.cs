using HR.Infrastructure.Abstractions;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeOffboardingReportReader : IOffboardingReportReader
{
    private readonly IReadOnlyList<OffboardingReportItem> _items;

    public FakeOffboardingReportReader(IReadOnlyList<OffboardingReportItem> items)
    {
        _items = items;
    }

    public Guid? LastCompanyId { get; private set; }
    public bool WasCalled { get; private set; }

    public Task<IReadOnlyList<OffboardingReportItem>> GetOffboardingReportAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        WasCalled = true;

        return Task.FromResult(_items);
    }
}

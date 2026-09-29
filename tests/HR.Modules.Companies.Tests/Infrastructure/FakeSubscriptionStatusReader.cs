using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class FakeSubscriptionStatusReader : ISubscriptionStatusReader
{
    public SubscriptionStatusSnapshot SnapshotToReturn { get; set; } =
        new(SubscriptionStatus.Trial, IsReadOnly: false, TrialDaysRemaining: 14);

    public Guid? LastCompanyId { get; private set; }

    public bool WasCalled { get; private set; }

    public Task<SubscriptionStatusSnapshot> GetStatusAsync(Guid companyId, CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        WasCalled = true;
        return Task.FromResult(SnapshotToReturn);
    }
}

using HR.Modules.Companies.Contracts;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeSubscriptionStatusReader : ISubscriptionStatusReader
{
    /// <summary>Per-company override. Companies not present here default to <see cref="DefaultStatus"/>.</summary>
    public Dictionary<Guid, SubscriptionStatus> Statuses { get; } = [];

    public SubscriptionStatus DefaultStatus { get; set; } = SubscriptionStatus.Active;

    public Task<SubscriptionStatusSnapshot> GetStatusAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var status = Statuses.GetValueOrDefault(companyId, DefaultStatus);
        return Task.FromResult(new SubscriptionStatusSnapshot(status, IsReadOnly: false, TrialDaysRemaining: 0));
    }
}

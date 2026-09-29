namespace HR.Modules.Companies.Contracts;

public interface ISubscriptionStatusReader
{
    Task<SubscriptionStatusSnapshot> GetStatusAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed record SubscriptionStatusSnapshot(
    SubscriptionStatus Status,
    bool IsReadOnly,
    int TrialDaysRemaining);

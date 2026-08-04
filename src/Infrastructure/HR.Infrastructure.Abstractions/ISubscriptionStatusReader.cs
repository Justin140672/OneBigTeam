namespace HR.Infrastructure.Abstractions;

// Port used by any module/UI needing the current company's trial/subscription status without a
// direct reference to HR.Modules.Companies (which owns the CustomerSubscription aggregate).
// Implemented in HR.Modules.Companies (SubscriptionStatusReader) and consumed by:
//   - HR.Web's AppSession / TrialBanner (read-only-mode + trial countdown UI)
//   - HR.Modules.Companies' own StartSubscriptionTask (Getting Started checklist, optional item)
public interface ISubscriptionStatusReader
{
    Task<SubscriptionStatusSnapshot> GetStatusAsync(Guid companyId, CancellationToken cancellationToken);
}

// IsReadOnly is true only once a trial has expired without an active paid subscription taking
// over (Status == TrialExpired). PastDue/Canceled read-only enforcement is deferred to the
// dedicated read-only middleware landing in a later phase of this epic.
// TrialDaysRemaining is 0 whenever Status != Trial.
public sealed record SubscriptionStatusSnapshot(
    SubscriptionStatus Status,
    bool IsReadOnly,
    int TrialDaysRemaining);

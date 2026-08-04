using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Features.GetSubscriptionStatus;

internal sealed record GetSubscriptionStatusResponse(
    SubscriptionStatus Status,
    bool IsReadOnly,
    int TrialDaysRemaining);

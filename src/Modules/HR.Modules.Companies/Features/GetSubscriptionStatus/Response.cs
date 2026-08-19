using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Companies.Features.GetSubscriptionStatus;

internal sealed record GetSubscriptionStatusResponse(
    SubscriptionStatus Status,
    bool IsReadOnly,
    int TrialDaysRemaining);

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Companies.Features.GetSubscriptionDetails;

internal sealed record GetSubscriptionDetailsResponse(
    SubscriptionStatus Status,
    string? PlanName,
    int ActiveEmployeeCount,
    DateTimeOffset? NextBillingDate,
    bool CancelAtPeriodEnd);

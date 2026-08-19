using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Web.Models;

public sealed record GetSubscriptionStatusResponse(
    SubscriptionStatus Status,
    bool IsReadOnly,
    int TrialDaysRemaining);

public sealed record CreateCheckoutSessionResponse(string CheckoutUrl);

public sealed record GetSubscriptionDetailsResponse(
    SubscriptionStatus Status,
    string? PlanName,
    int ActiveEmployeeCount,
    DateTimeOffset? NextBillingDate,
    bool CancelAtPeriodEnd);

public sealed record CancelSubscriptionResponse(bool CancelAtPeriodEnd);

public sealed record ResumeSubscriptionResponse(bool CancelAtPeriodEnd);

public sealed record BillingPortalResponse(string PortalUrl);

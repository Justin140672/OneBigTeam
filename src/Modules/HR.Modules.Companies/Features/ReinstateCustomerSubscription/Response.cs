namespace HR.Modules.Companies.Features.ReinstateCustomerSubscription;

internal sealed record ReinstateCustomerSubscriptionResponse(Guid CompanyId, string Status, bool CancelAtPeriodEnd);

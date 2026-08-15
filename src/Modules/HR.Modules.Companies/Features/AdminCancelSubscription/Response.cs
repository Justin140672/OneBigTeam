namespace HR.Modules.Companies.Features.AdminCancelSubscription;

internal sealed record AdminCancelSubscriptionResponse(Guid CompanyId, bool CancelAtPeriodEnd);

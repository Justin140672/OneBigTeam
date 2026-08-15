namespace HR.Modules.Companies.Features.AdminCancelSubscription;

internal sealed record AdminCancelSubscriptionRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

namespace HR.Modules.Companies.Features.ReinstateCustomerSubscription;

internal sealed record ReinstateCustomerSubscriptionRequest
{
    public Guid CompanyId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

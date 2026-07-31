namespace HR.Marketing.Services;

public static class PricingCtaResolver
{
    public const string StartFreeTrial = "Start Free Trial";
    public const string ContactSales = "Contact Sales";

    public static PricingCtaResult Resolve(int activeEmployees, int largeOrganisationThreshold)
    {
        var isLargeOrganisation = activeEmployees > largeOrganisationThreshold;

        return isLargeOrganisation
            ? new PricingCtaResult(
                ContactSales,
                StartFreeTrial,
                "Larger organisations often benefit from a guided implementation. We'd be happy to help you plan your rollout.",
                IsLargeOrganisation: true)
            : new PricingCtaResult(
                StartFreeTrial,
                string.Empty,
                "Perfect for self-service setup. You can start your free trial today.",
                IsLargeOrganisation: false);
    }
}

public readonly record struct PricingCtaResult(
    string PrimaryLabel,
    string SecondaryLabel,
    string SupportingMessage,
    bool IsLargeOrganisation);

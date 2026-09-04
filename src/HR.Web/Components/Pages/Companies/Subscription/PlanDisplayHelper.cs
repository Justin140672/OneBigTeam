namespace HR.Web.Components.Pages.Companies.Subscription;

/// <summary>
/// Maps the raw plan/price identifier returned by the subscription-details API to a
/// customer-facing plan name.
///
/// Source: as of 2026-09-04, HR.Modules.Companies only recognises a single configured Stripe
/// price (see StripeOptions.PriceId / GetSubscriptionDetailsHandler), which it already maps to
/// "Standard Plan" server-side. If a company's stored PriceId does not match the currently
/// configured price (e.g. a legacy/migrated price, or local dev-stub identifiers such as
/// "dev-stub-price"), the API falls back to returning the raw Stripe price id verbatim rather
/// than inventing a plan catalogue. This helper catches that fallback client-side so the raw id
/// is never shown to a user.
///
/// There is no multi-plan catalogue anywhere in the codebase today, so this is a best-effort
/// fallback rather than a definitive mapping — extend the dictionary below if/when additional
/// named plans are introduced.
/// </summary>
public static class PlanDisplayHelper
{
    private static readonly Dictionary<string, string> KnownPlanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Local/dev-only Stripe stand-in used when no real Stripe configuration is present.
        ["dev-stub-price"] = "Standard Plan (dev)",
    };

    public static string GetDisplayName(string? rawPlanName)
    {
        if (string.IsNullOrWhiteSpace(rawPlanName))
            return "No plan";

        if (KnownPlanNames.TryGetValue(rawPlanName, out var known))
            return known;

        // Anything that still looks like a raw Stripe price id (or another internal
        // identifier) rather than a human-readable name — don't show it verbatim.
        var looksLikeRawId =
            rawPlanName.StartsWith("price_", StringComparison.OrdinalIgnoreCase) ||
            rawPlanName.Contains("dev-stub", StringComparison.OrdinalIgnoreCase) ||
            (!rawPlanName.Contains(' ') && rawPlanName.Any(char.IsDigit));

        return looksLikeRawId ? "Custom plan" : rawPlanName;
    }
}

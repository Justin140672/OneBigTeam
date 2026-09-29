namespace HR.Web.Components.Pages.Companies.Subscription;

public static class PlanDisplayHelper
{
    private static readonly Dictionary<string, string> KnownPlanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dev-stub-price"] = "Standard Plan (dev)",
    };

    public static string GetDisplayName(string? rawPlanName)
    {
        if (string.IsNullOrWhiteSpace(rawPlanName))
            return "No plan";

        if (KnownPlanNames.TryGetValue(rawPlanName, out var known))
            return known;

        var looksLikeRawId =
            rawPlanName.StartsWith("price_", StringComparison.OrdinalIgnoreCase) ||
            rawPlanName.Contains("dev-stub", StringComparison.OrdinalIgnoreCase) ||
            (!rawPlanName.Contains(' ') && rawPlanName.Any(char.IsDigit));

        return looksLikeRawId ? "Custom plan" : rawPlanName;
    }
}

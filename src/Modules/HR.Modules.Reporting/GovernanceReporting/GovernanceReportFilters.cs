namespace HR.Modules.Reporting.GovernanceReporting;

/// <summary>ADM-08: shared filter-value validation for the governance reports.</summary>
internal static class GovernanceReportFilters
{
    public static readonly string[] Statuses = ["Success", "Failed"];

    public static bool IsValidStatus(string? value) =>
        value is null || Statuses.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase));
}

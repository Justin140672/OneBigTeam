namespace HR.Modules.Employees.Features.GetEqualityDiversityReport;

/// <summary>
/// Configuration for the anonymous Equality &amp; Diversity workforce report.
/// </summary>
internal sealed class EqualityDiversityReportOptions
{
    public const string SectionName = "Employees:EqualityDiversityReport";

    /// <summary>
    /// Minimum-group suppression threshold. Any category value with a non-zero head count
    /// below this number is collapsed into the "Not reported" bucket so that a small group
    /// can never be used to identify an individual. Applied consistently to every dimension.
    /// Values below 2 are treated as the safe default (5).
    /// </summary>
    public int MinimumGroupSize { get; set; } = 5;

    public int ResolvedMinimumGroupSize => MinimumGroupSize < 2 ? 5 : MinimumGroupSize;
}

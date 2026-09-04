namespace HR.Modules.Employees.Features.GetEqualityDiversityReport;

/// <summary>
/// Aggregated, anonymous workforce equality statistics. Contains counts and percentages only —
/// never employee identifiers or row-level data. Small groups are suppressed (see
/// <see cref="EqualityDiversityReportOptions"/>).
/// </summary>
internal sealed record GetEqualityDiversityReportResponse(
    int TotalEmployees,
    int MinimumGroupSize,
    IReadOnlyList<EqualityReportDimension> Dimensions);

internal sealed record EqualityReportDimension(
    string Key,
    string Name,
    IReadOnlyList<EqualityReportRow> Rows);

internal sealed record EqualityReportRow(
    string Value,
    int Count,
    decimal Percentage,
    bool Suppressed);

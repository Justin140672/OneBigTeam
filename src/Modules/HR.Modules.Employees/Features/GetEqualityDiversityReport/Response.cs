namespace HR.Modules.Employees.Features.GetEqualityDiversityReport;

internal sealed record GetEqualityDiversityReportResponse(
    int TotalEmployees,
    int RespondentCount,
    decimal RespondentPercentage,
    DateOnly ReportingDate,
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

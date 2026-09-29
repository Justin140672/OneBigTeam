namespace HR.Modules.Employees.Features.GetEqualityDiversityReport;

internal sealed class EqualityDiversityReportOptions
{
    public const string SectionName = "Employees:EqualityDiversityReport";

    public int MinimumGroupSize { get; set; } = 5;

    public int ResolvedMinimumGroupSize => MinimumGroupSize < 2 ? 5 : MinimumGroupSize;
}

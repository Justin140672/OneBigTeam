namespace HR.Modules.Reporting.ReportRegistry;

/// <summary>
/// Central bounded-result configuration for the report catalogue (REP-05). Every Get*Report and
/// Export*Report handler that materialises a row list uses these constants instead of its own
/// scattered magic number, so display and export caps are consistent and auditable in one place.
///
/// Reports that already implement true server-side pagination (e.g. GetEmployeeDirectoryReport,
/// GetEmployeeLeaverReport, GetEmployeeStarterReport) use <see cref="ExportRowLimit"/> only for
/// their paired Export*Report handler, since the live Get* endpoint already returns bounded pages
/// with an accurate TotalCount.
/// </summary>
internal static class ReportLimits
{
    /// <summary>
    /// Maximum number of rows returned by a live (on-screen) Get*Report response for reports that
    /// do not implement full server-side pagination. Reports that reach this cap must report
    /// <c>IsTruncated = true</c> alongside the true <c>TotalCount</c> of the filtered set.
    /// </summary>
    public const int DisplayRowLimit = 20_000;

    /// <summary>
    /// Maximum number of rows returned by an Export*Report handler. Deliberately larger than
    /// <see cref="DisplayRowLimit"/> since exports are the primary consumption path for some
    /// reports and are not constrained by on-screen grid rendering. Exports that reach this cap
    /// must report <c>IsTruncated = true</c>.
    /// </summary>
    public const int ExportRowLimit = 50_000;
}

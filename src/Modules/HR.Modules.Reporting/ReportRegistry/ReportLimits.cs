namespace HR.Modules.Reporting.ReportRegistry;

internal static class ReportLimits
{
    public const int DisplayRowLimit = 20_000;

    /// <summary>
    /// Maximum number of rows returned by an Export*Report handler. Deliberately larger than
    /// <see cref="DisplayRowLimit"/> since exports are the primary consumption path for some
    /// reports and are not constrained by on-screen grid rendering. Exports that reach this cap
    /// must report <c>IsTruncated = true</c>.
    /// </summary>
    public const int ExportRowLimit = 50_000;
}

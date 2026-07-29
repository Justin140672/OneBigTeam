namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Generic, report-agnostic export abstraction shared by every reporting export button.
/// Implementations live in HR.Infrastructure (Reporting/export generation is a cross-cutting
/// concern per the architecture spec) and are consumed by report handlers in
/// HR.Modules.Reporting. Consumers must pass the FULL filtered result set (not just the
/// current page) so exports respect current filters without truncating to one page.
/// </summary>
public interface IReportExporter
{
    /// <summary>
    /// Renders tabular report data into the requested export format and returns the resulting
    /// file bytes, content type and suggested file name.
    /// </summary>
    ReportExportFile Export(ReportExportFormat format, ReportExportData data);
}

public enum ReportExportFormat
{
    Csv = 1,
    Excel = 2,
    Pdf = 3,
}

/// <summary>
/// Generic, column-oriented tabular payload. Every future report reuses this shape — it must
/// never carry report-specific typed columns.
/// </summary>
public sealed record ReportExportData(
    string ReportTitle,
    IReadOnlyList<string> ColumnHeaders,
    IReadOnlyList<IReadOnlyList<string?>> Rows);

public sealed record ReportExportFile(
    byte[] Content,
    string ContentType,
    string FileName);

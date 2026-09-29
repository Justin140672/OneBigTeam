namespace HR.Infrastructure.Abstractions;

public interface IReportExporter
{
    ReportExportFile Export(ReportExportFormat format, ReportExportData data);
}

public enum ReportExportFormat
{
    Csv = 1,
    Excel = 2,
    Pdf = 3,
}

public sealed record ReportExportData(
    string ReportTitle,
    IReadOnlyList<string> ColumnHeaders,
    IReadOnlyList<IReadOnlyList<string?>> Rows);

public sealed record ReportExportFile(
    byte[] Content,
    string ContentType,
    string FileName);

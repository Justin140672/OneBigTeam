using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IReportExporter"/> — records the format/data it was called
/// with and returns a pre-configured file, so export handler tests can assert both the inbound
/// mapping and the outbound response without depending on the real ClosedXML/QuestPDF/CSV code.
/// </summary>
internal sealed class FakeReportExporter : IReportExporter
{
    private readonly ReportExportFile _file;

    public FakeReportExporter(ReportExportFile? file = null)
    {
        _file = file ?? new ReportExportFile([1, 2, 3], "text/csv", "report.csv");
    }

    public ReportExportFormat? LastFormat { get; private set; }
    public ReportExportData? LastData { get; private set; }

    public ReportExportFile Export(ReportExportFormat format, ReportExportData data)
    {
        LastFormat = format;
        LastData = data;
        return _file;
    }
}

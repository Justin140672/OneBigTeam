using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

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

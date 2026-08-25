using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportCompanyDocumentAcknowledgementReport;
using HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportCompanyDocumentAcknowledgementReportHandlerTests
{
    private static CompanyDocumentAcknowledgementReportItem BuildItem(Guid employeeId) =>
        new(Guid.NewGuid(), "Employee Handbook", employeeId, Acknowledged: true, new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeCompanyDocumentAcknowledgementReportReader([BuildItem(employeeId)]);
        var getHandler = new GetCompanyDocumentAcknowledgementReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportCompanyDocumentAcknowledgementReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportCompanyDocumentAcknowledgementReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Company Document Acknowledgement Report", exporter.LastData!.ReportTitle);
        Assert.Equal(["Document", "Employee", "Acknowledged", "Acknowledged At"], exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Employee Handbook", row[0]);
        Assert.Equal("True", row[2]);
        Assert.Equal("2026-03-01 09:30", row[3]);
    }

    [Fact]
    public async Task HandleAsync_Exports_Empty_Rows_When_No_Items()
    {
        var reader = new FakeCompanyDocumentAcknowledgementReportReader([]);
        var getHandler = new GetCompanyDocumentAcknowledgementReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportCompanyDocumentAcknowledgementReportHandler(getHandler, exporter, TestReportExportAuditor.Create());

        var result = await handler.HandleAsync(
            new ExportCompanyDocumentAcknowledgementReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(exporter.LastData!.Rows);
    }
}

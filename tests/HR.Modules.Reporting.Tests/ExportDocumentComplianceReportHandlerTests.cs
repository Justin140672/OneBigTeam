using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportDocumentComplianceReport;
using HR.Modules.Reporting.Features.GetDocumentComplianceReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportDocumentComplianceReportHandlerTests
{
    private static DocumentComplianceReportItem BuildItem(Guid employeeId) =>
        new(
            employeeId,
            PositionProfileId: null,
            RequiredCount: 5,
            UploadedCount: 3,
            MissingCount: 2,
            ExpiringSoonCount: 1,
            ExpiredCount: 0,
            MissingDocumentTypeNames: ["Passport", "Visa"]);

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeDocumentComplianceReportReader([BuildItem(employeeId)]);
        var getHandler = new GetDocumentComplianceReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportDocumentComplianceReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(
            new ExportDocumentComplianceReportRequest(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Document Compliance Report", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Employee", "Required", "Uploaded", "Missing", "Expiring Soon", "Expired", "Missing Documents"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("2", row[3]);
        Assert.Equal("Passport; Visa", row[6]);
    }

    [Fact]
    public async Task HandleAsync_Exports_Empty_Rows_When_No_Items()
    {
        var reader = new FakeDocumentComplianceReportReader([]);
        var getHandler = new GetDocumentComplianceReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportDocumentComplianceReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(
            new ExportDocumentComplianceReportRequest(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(exporter.LastData!.Rows);
    }
}

using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportAssetAssignmentReport;
using HR.Modules.Reporting.Features.GetAssetAssignmentReport;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportAssetAssignmentReportHandlerTests
{
    private static AssetAssignmentReportItem BuildItem(Guid employeeId) =>
        new(
            Guid.NewGuid(),
            employeeId,
            "AST-001 - Laptop",
            "SN123",
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            "Assigned");

    [Fact]
    public async Task HandleAsync_Exports_Rows_From_GetHandler_Result()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeAssetAssignmentReportReader([BuildItem(employeeId)]);
        var getHandler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportAssetAssignmentReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(new ExportAssetAssignmentReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Asset Assignment Report", exporter.LastData!.ReportTitle);
        Assert.Equal(
            ["Employee", "Asset", "Serial Number", "Assigned Date", "Return Status"],
            exporter.LastData.ColumnHeaders);
        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal(employeeId.ToString(), row[0]);
        Assert.Equal("AST-001 - Laptop", row[1]);
        Assert.Equal("SN123", row[2]);
        Assert.Equal("2026-08-01", row[3]);
        Assert.Equal("Assigned", row[4]);
    }

    [Fact]
    public async Task HandleAsync_Exports_Empty_Rows_When_No_Assignments()
    {
        var reader = new FakeAssetAssignmentReportReader([]);
        var getHandler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportAssetAssignmentReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(new ExportAssetAssignmentReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(exporter.LastData!.Rows);
    }

    [Fact]
    public async Task HandleAsync_Forwards_Requested_Format_To_Exporter()
    {
        var reader = new FakeAssetAssignmentReportReader([BuildItem(Guid.NewGuid())]);
        var getHandler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportAssetAssignmentReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(
            new ExportAssetAssignmentReportRequest(Guid.NewGuid(), ReportExportFormat.Pdf), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReportExportFormat.Pdf, exporter.LastFormat);
    }

    [Fact]
    public async Task HandleAsync_Propagates_IsTruncated_And_TotalCount_From_GetHandler()
    {
        const int overLimitBy = 500;
        var items = Enumerable.Range(0, ReportLimits.DisplayRowLimit + overLimitBy)
            .Select(_ => BuildItem(Guid.NewGuid()))
            .ToList();
        var reader = new FakeAssetAssignmentReportReader(items);
        var getHandler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportAssetAssignmentReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(new ExportAssetAssignmentReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(ReportLimits.DisplayRowLimit + overLimitBy, result.Value.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Below_Limit_Reports_Not_Truncated()
    {
        var reader = new FakeAssetAssignmentReportReader([BuildItem(Guid.NewGuid())]);
        var getHandler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader());
        var exporter = new FakeReportExporter();
        var handler = new ExportAssetAssignmentReportHandler(getHandler, exporter);

        var result = await handler.HandleAsync(new ExportAssetAssignmentReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(1, result.Value.TotalCount);
    }
}

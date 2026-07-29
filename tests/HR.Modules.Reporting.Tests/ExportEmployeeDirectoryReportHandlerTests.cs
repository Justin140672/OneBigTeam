using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportEmployeeDirectoryReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportEmployeeDirectoryReportHandlerTests
{
    private static readonly string[] ExpectedColumnHeaders =
    [
        "Employee Number", "Name", "Department", "Position", "Manager",
        "Employment Type", "Start Date", "Status", "Work Location", "Email",
    ];

    private static EmployeeDirectoryReportItem BuildItem(Guid employeeId) =>
        new(
            employeeId,
            "EMP-001",
            "Alice Smith",
            "Engineering",
            "Senior Developer",
            "Jane Manager",
            "Full Time",
            new DateOnly(2026, 1, 1),
            "Active",
            "London",
            "alice@example.com");

    private static ExportEmployeeDirectoryReportRequest BuildRequest(
        Guid companyId, ReportExportFormat format = ReportExportFormat.Csv) =>
        new(
            CompanyId: companyId,
            DepartmentId: null,
            LocationId: null,
            PositionProfileId: null,
            ManagerId: null,
            EmploymentTypeId: null,
            DateRangeStart: null,
            DateRangeEnd: null,
            EmployeeStatus: null,
            Format: format);

    [Fact]
    public async Task HandleAsync_Builds_ReportExportData_With_Expected_Column_Headers_And_Row_Values()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeEmployeeDirectoryReader([BuildItem(employeeId)]);
        var exporter = new FakeReportExporter();
        var handler = new ExportEmployeeDirectoryReportHandler(reader, exporter);

        await handler.HandleAsync(BuildRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.NotNull(exporter.LastData);
        Assert.Equal("Employee Directory", exporter.LastData!.ReportTitle);
        Assert.Equal(ExpectedColumnHeaders, exporter.LastData.ColumnHeaders);

        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal(
            new List<string?>
            {
                "EMP-001", "Alice Smith", "Engineering", "Senior Developer", "Jane Manager",
                "Full Time", "2026-01-01", "Active", "London", "alice@example.com",
            },
            row);
    }

    [Fact]
    public async Task HandleAsync_Uses_A_Single_Page_Large_Enough_To_Cover_All_Rows()
    {
        var reader = new FakeEmployeeDirectoryReader([]);
        var exporter = new FakeReportExporter();
        var handler = new ExportEmployeeDirectoryReportHandler(reader, exporter);

        await handler.HandleAsync(BuildRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.NotNull(reader.LastPagination);
        Assert.Equal(1, reader.LastPagination!.PageNumber);
        Assert.Equal(50_000, reader.LastPagination.PageSize);
    }

    [Fact]
    public async Task HandleAsync_Forwards_Request_Format_To_Exporter()
    {
        var reader = new FakeEmployeeDirectoryReader([]);
        var exporter = new FakeReportExporter();
        var handler = new ExportEmployeeDirectoryReportHandler(reader, exporter);

        await handler.HandleAsync(BuildRequest(Guid.NewGuid(), ReportExportFormat.Pdf), CancellationToken.None);

        Assert.Equal(ReportExportFormat.Pdf, exporter.LastFormat);
    }

    [Fact]
    public async Task HandleAsync_Returns_Exporter_File_In_Response()
    {
        var reader = new FakeEmployeeDirectoryReader([]);
        var file = new ReportExportFile([9, 9, 9], "application/pdf", "employee-directory.pdf");
        var exporter = new FakeReportExporter(file);
        var handler = new ExportEmployeeDirectoryReportHandler(reader, exporter);

        var result = await handler.HandleAsync(BuildRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(file, result.Value!.File);
    }
}

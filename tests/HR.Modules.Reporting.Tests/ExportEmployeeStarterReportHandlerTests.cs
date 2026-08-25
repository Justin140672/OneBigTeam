using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportEmployeeStarterReport;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class ExportEmployeeStarterReportHandlerTests
{
    private static readonly string[] ExpectedColumnHeaders =
    [
        "Name", "Start Date", "Recruiter", "Department", "Position", "Onboarding Status", "Probation Status",
    ];

    private static EmployeeStarterReportItem BuildItem(Guid employeeId) =>
        new(
            employeeId,
            "Bob Jones",
            new DateOnly(2026, 6, 1),
            "Jamie Recruiter",
            "Engineering",
            "Junior Developer",
            "In Progress",
            "Ongoing");

    private static ExportEmployeeStarterReportRequest BuildRequest(
        Guid companyId, ReportExportFormat format = ReportExportFormat.Csv) =>
        new(
            CompanyId: companyId,
            DepartmentId: null,
            LocationId: null,
            PositionProfileId: null,
            EmploymentTypeId: null,
            DateRangeStart: null,
            DateRangeEnd: null,
            Format: format);

    [Fact]
    public async Task HandleAsync_Builds_ReportExportData_With_Expected_Column_Headers_And_Row_Values()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeEmployeeStarterReader([BuildItem(employeeId)]);
        var exporter = new FakeReportExporter();
        var handler = new ExportEmployeeStarterReportHandler(reader, exporter);

        await handler.HandleAsync(BuildRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.NotNull(exporter.LastData);
        Assert.Equal("Employee Starter Report", exporter.LastData!.ReportTitle);
        Assert.Equal(ExpectedColumnHeaders, exporter.LastData.ColumnHeaders);

        var row = Assert.Single(exporter.LastData.Rows);
        Assert.Equal("Bob Jones", row[0]);
        Assert.Equal("2026-06-01", row[1]);
        Assert.Equal("Jamie Recruiter", row[2]);
        Assert.Equal("In Progress", row[5]);
        Assert.Equal("Ongoing", row[6]);
    }

    [Fact]
    public async Task HandleAsync_Uses_A_Single_Page_Large_Enough_To_Cover_All_Rows()
    {
        var reader = new FakeEmployeeStarterReader([]);
        var exporter = new FakeReportExporter();
        var handler = new ExportEmployeeStarterReportHandler(reader, exporter);

        await handler.HandleAsync(BuildRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.NotNull(reader.LastPagination);
        Assert.Equal(1, reader.LastPagination!.PageNumber);
        Assert.Equal(ReportLimits.ExportRowLimit, reader.LastPagination.PageSize);
    }

    [Fact]
    public async Task HandleAsync_Forwards_Requested_Format_To_Exporter()
    {
        var reader = new FakeEmployeeStarterReader([]);
        var exporter = new FakeReportExporter();
        var handler = new ExportEmployeeStarterReportHandler(reader, exporter);

        await handler.HandleAsync(BuildRequest(Guid.NewGuid(), ReportExportFormat.Pdf), CancellationToken.None);

        Assert.Equal(ReportExportFormat.Pdf, exporter.LastFormat);
    }

    [Fact]
    public async Task HandleAsync_TotalCount_At_Or_Below_ExportRowLimit_Is_Not_Truncated()
    {
        var reader = new FakeEmployeeStarterReader([BuildItem(Guid.NewGuid())], totalCount: ReportLimits.ExportRowLimit);
        var exporter = new FakeReportExporter();
        var handler = new ExportEmployeeStarterReportHandler(reader, exporter);

        var result = await handler.HandleAsync(BuildRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(ReportLimits.ExportRowLimit, result.Value.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_TotalCount_Above_ExportRowLimit_Is_Truncated()
    {
        var totalCount = ReportLimits.ExportRowLimit + 1;
        var reader = new FakeEmployeeStarterReader([BuildItem(Guid.NewGuid())], totalCount: totalCount);
        var exporter = new FakeReportExporter();
        var handler = new ExportEmployeeStarterReportHandler(reader, exporter);

        var result = await handler.HandleAsync(BuildRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsTruncated);
        Assert.Equal(totalCount, result.Value.TotalCount);
    }
}

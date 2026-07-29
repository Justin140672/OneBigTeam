using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportEmployeeDirectoryReport;

namespace HR.Modules.Reporting.Tests;

public class ExportEmployeeDirectoryReportValidatorTests
{
    private readonly ExportEmployeeDirectoryReportValidator _validator = new();

    private static ExportEmployeeDirectoryReportRequest ValidRequest() =>
        new(
            CompanyId: Guid.NewGuid(),
            DepartmentId: null,
            LocationId: null,
            PositionProfileId: null,
            ManagerId: null,
            EmploymentTypeId: null,
            DateRangeStart: null,
            DateRangeEnd: null,
            EmployeeStatus: null,
            Format: ReportExportFormat.Csv);

    [Fact]
    public void Should_Not_Have_Error_For_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_CompanyId_Is_Empty()
    {
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportEmployeeDirectoryReportRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_DateRangeEnd_Is_Before_DateRangeStart()
    {
        var request = ValidRequest() with
        {
            DateRangeStart = new DateOnly(2026, 6, 1),
            DateRangeEnd = new DateOnly(2026, 5, 1),
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportEmployeeDirectoryReportRequest.DateRangeEnd));
    }

    [Theory]
    [InlineData(ReportExportFormat.Csv)]
    [InlineData(ReportExportFormat.Excel)]
    [InlineData(ReportExportFormat.Pdf)]
    public void Should_Not_Have_Error_For_Any_Defined_Format(ReportExportFormat format)
    {
        var request = ValidRequest() with { Format = format };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_Format_Is_Not_A_Defined_Enum_Value()
    {
        var request = ValidRequest() with { Format = (ReportExportFormat)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportEmployeeDirectoryReportRequest.Format));
    }
}

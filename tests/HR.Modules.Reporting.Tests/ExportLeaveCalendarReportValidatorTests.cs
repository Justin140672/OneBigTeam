using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportLeaveCalendarReport;

namespace HR.Modules.Reporting.Tests;

public class ExportLeaveCalendarReportValidatorTests
{
    private readonly ExportLeaveCalendarReportValidator _validator = new();

    private static ExportLeaveCalendarReportRequest ValidRequest() =>
        new(CompanyId: Guid.NewGuid(), Year: 2026, Month: 6, Format: ReportExportFormat.Csv);

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportLeaveCalendarReportRequest.CompanyId));
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2100)]
    public void Should_Not_Have_Error_When_Year_Is_At_Boundary(int year)
    {
        var request = ValidRequest() with { Year = year };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void Should_Have_Error_When_Year_Is_Outside_Boundary(int year)
    {
        var request = ValidRequest() with { Year = year };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportLeaveCalendarReportRequest.Year));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Should_Not_Have_Error_When_Month_Is_At_Boundary(int month)
    {
        var request = ValidRequest() with { Month = month };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Should_Have_Error_When_Month_Is_Outside_Boundary(int month)
    {
        var request = ValidRequest() with { Month = month };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportLeaveCalendarReportRequest.Month));
    }

    [Fact]
    public void Should_Have_Error_When_Format_Is_Not_A_Defined_Enum_Value()
    {
        var request = ValidRequest() with { Format = (ReportExportFormat)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportLeaveCalendarReportRequest.Format));
    }
}

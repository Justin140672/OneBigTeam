using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportLeaveSummaryReport;
using HR.Modules.Reporting.Features.GetLeaveSummaryReport;

namespace HR.Modules.Reporting.Tests;

public class ExportLeaveSummaryReportValidatorTests
{
    private readonly ExportLeaveSummaryReportValidator _validator = new();

    private static ExportLeaveSummaryReportRequest ValidRequest() =>
        new(CompanyId: Guid.NewGuid(), PolicyYear: null, DepartmentId: null);

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportLeaveSummaryReportRequest.CompanyId));
    }

    [Theory]
    [InlineData(LeaveSummaryGroupBy.Employee)]
    [InlineData(LeaveSummaryGroupBy.Department)]
    [InlineData(LeaveSummaryGroupBy.LeaveType)]
    public void Should_Not_Have_Error_For_Any_Defined_GroupBy(LeaveSummaryGroupBy groupBy)
    {
        var request = ValidRequest() with { GroupBy = groupBy };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_GroupBy_Is_Not_A_Defined_Enum_Value()
    {
        var request = ValidRequest() with { GroupBy = (LeaveSummaryGroupBy)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportLeaveSummaryReportRequest.GroupBy));
    }

    [Fact]
    public void Should_Have_Error_When_Format_Is_Not_A_Defined_Enum_Value()
    {
        var request = ValidRequest() with { Format = (ReportExportFormat)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportLeaveSummaryReportRequest.Format));
    }

    [Fact]
    public void Should_Not_Have_Error_When_PolicyYear_Is_Null()
    {
        var request = ValidRequest() with { PolicyYear = null };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2100)]
    public void Should_Not_Have_Error_When_PolicyYear_Is_At_Boundary(int policyYear)
    {
        var request = ValidRequest() with { PolicyYear = policyYear };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void Should_Have_Error_When_PolicyYear_Is_Outside_Boundary(int policyYear)
    {
        var request = ValidRequest() with { PolicyYear = policyYear };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportLeaveSummaryReportRequest.PolicyYear));
    }
}

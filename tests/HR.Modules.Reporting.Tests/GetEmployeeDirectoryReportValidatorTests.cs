using HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;

namespace HR.Modules.Reporting.Tests;

public class GetEmployeeDirectoryReportValidatorTests
{
    private readonly GetEmployeeDirectoryReportValidator _validator = new();

    private static GetEmployeeDirectoryReportRequest ValidRequest() =>
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
            Page: 1,
            PageSize: 20);

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDirectoryReportRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_Page_Is_Less_Than_One()
    {
        var request = ValidRequest() with { Page = 0 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDirectoryReportRequest.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Should_Have_Error_When_PageSize_Is_Outside_Allowed_Range(int pageSize)
    {
        var request = ValidRequest() with { PageSize = pageSize };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDirectoryReportRequest.PageSize));
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDirectoryReportRequest.DateRangeEnd));
    }

    [Fact]
    public void Should_Not_Have_Error_When_DateRangeEnd_Equals_DateRangeStart()
    {
        var request = ValidRequest() with
        {
            DateRangeStart = new DateOnly(2026, 6, 1),
            DateRangeEnd = new DateOnly(2026, 6, 1),
        };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

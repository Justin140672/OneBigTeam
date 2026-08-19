using HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;

namespace HR.Modules.Reporting.Tests;

public class GetHrHeadcountSummaryReportValidatorTests
{
    private readonly GetHrHeadcountSummaryReportValidator _validator = new();

    private static GetHrHeadcountSummaryReportRequest ValidRequest() =>
        new(Guid.NewGuid(), null, null, null, null);

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetHrHeadcountSummaryReportRequest.CompanyId));
    }

    [Fact]
    public void Should_Not_Have_Error_When_Optional_Filters_Are_Provided()
    {
        var request = ValidRequest() with
        {
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            EmployeeStatus = "Active",
        };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

using HR.Modules.Employees.Features.GetEmployeeTimeline;

namespace HR.Modules.Employees.Tests;

public class GetEmployeeTimelineValidatorTests
{
    private static GetEmployeeTimelineRequest ValidRequest() =>
        new()
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PageNumber = 1,
            PageSize = 20,
        };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new GetEmployeeTimelineValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new GetEmployeeTimelineValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeTimelineRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new GetEmployeeTimelineValidator();
        var request = ValidRequest() with { EmployeeId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeTimelineRequest.EmployeeId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Fails_When_PageNumber_Is_Less_Than_One(int pageNumber)
    {
        var validator = new GetEmployeeTimelineValidator();
        var request = ValidRequest() with { PageNumber = pageNumber };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeTimelineRequest.PageNumber));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Validate_Fails_When_PageSize_Is_Out_Of_Range(int pageSize)
    {
        var validator = new GetEmployeeTimelineValidator();
        var request = ValidRequest() with { PageSize = pageSize };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeTimelineRequest.PageSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void Validate_Passes_For_Boundary_PageSize_Values(int pageSize)
    {
        var validator = new GetEmployeeTimelineValidator();
        var request = ValidRequest() with { PageSize = pageSize };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

}

using HR.Modules.Employees.Features.ListEmployees;

namespace HR.Modules.Employees.Tests;

public class ListEmployeesValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new ListEmployeesValidator();

        var result = validator.Validate(new ListEmployeesRequest
        {
            CompanyId = Guid.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListEmployeesRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_PageNumber_Is_Zero()
    {
        var validator = new ListEmployeesValidator();

        var result = validator.Validate(new ListEmployeesRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 0
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListEmployeesRequest.PageNumber));
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Exceeds_500()
    {
        var validator = new ListEmployeesValidator();

        var result = validator.Validate(new ListEmployeesRequest
        {
            CompanyId = Guid.NewGuid(),
            PageSize = 501
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListEmployeesRequest.PageSize));
    }

    [Fact]
    public void Validate_Fails_When_Search_Exceeds_Max_Length()
    {
        var validator = new ListEmployeesValidator();

        var result = validator.Validate(new ListEmployeesRequest
        {
            CompanyId = Guid.NewGuid(),
            Search = new string('a', 201)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListEmployeesRequest.Search));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new ListEmployeesValidator();

        var result = validator.Validate(new ListEmployeesRequest
        {
            CompanyId = Guid.NewGuid(),
            Search = "alice",
            DepartmentId = Guid.NewGuid(),
            Status = HR.Modules.Employees.Domain.EmploymentStatus.Active,
            PageNumber = 2,
            PageSize = 50
        });

        Assert.True(result.IsValid);
    }
}

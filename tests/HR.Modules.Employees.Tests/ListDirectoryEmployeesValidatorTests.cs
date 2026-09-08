using HR.Modules.Employees.Features.ListDirectoryEmployees;

namespace HR.Modules.Employees.Tests;

public class ListDirectoryEmployeesValidatorTests
{
    private readonly ListDirectoryEmployeesValidator _validator = new();

    private static ListDirectoryEmployeesRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        PageNumber = 1,
        PageSize = 50
    };

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDirectoryEmployeesRequest.CompanyId));
    }

    [Fact]
    public void Fails_When_PageNumber_Is_Zero()
    {
        var result = _validator.Validate(Valid() with { PageNumber = 0 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDirectoryEmployeesRequest.PageNumber));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Fails_When_PageSize_Out_Of_Range(int pageSize)
    {
        var result = _validator.Validate(Valid() with { PageSize = pageSize });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDirectoryEmployeesRequest.PageSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Passes_For_PageSize_Within_Inclusive_Range(int pageSize)
    {
        var result = _validator.Validate(Valid() with { PageSize = pageSize });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Fails_When_Search_Exceeds_200_Characters()
    {
        var result = _validator.Validate(Valid() with { Search = new string('a', 201) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDirectoryEmployeesRequest.Search));
    }

    [Fact]
    public void Passes_When_Search_Is_Exactly_200_Characters()
    {
        var result = _validator.Validate(Valid() with { Search = new string('a', 200) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Passes_When_Search_Is_Null()
    {
        var result = _validator.Validate(Valid() with { Search = null });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Passes_For_Fully_Populated_Valid_Request()
    {
        var result = _validator.Validate(new ListDirectoryEmployeesRequest
        {
            CompanyId = Guid.NewGuid(),
            Search = "alice",
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PageNumber = 2,
            PageSize = 25
        });

        Assert.True(result.IsValid);
    }
}

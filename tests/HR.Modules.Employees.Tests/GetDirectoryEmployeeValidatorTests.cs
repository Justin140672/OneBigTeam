using HR.Modules.Employees.Features.GetDirectoryEmployee;

namespace HR.Modules.Employees.Tests;

public class GetDirectoryEmployeeValidatorTests
{
    private readonly GetDirectoryEmployeeValidator _validator = new();

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetDirectoryEmployeeRequest { CompanyId = Guid.Empty, Id = Guid.NewGuid() });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetDirectoryEmployeeRequest.CompanyId));
    }

    [Fact]
    public void Fails_When_Id_Is_Empty()
    {
        var result = _validator.Validate(new GetDirectoryEmployeeRequest { CompanyId = Guid.NewGuid(), Id = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetDirectoryEmployeeRequest.Id));
    }

    [Fact]
    public void Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetDirectoryEmployeeRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }
}

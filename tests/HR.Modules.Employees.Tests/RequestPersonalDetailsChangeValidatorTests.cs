using HR.Modules.Employees.Features.RequestPersonalDetailsChange;

namespace HR.Modules.Employees.Tests;

public class RequestPersonalDetailsChangeValidatorTests
{
    [Fact]
    public void Validate_Fails_When_Notes_Is_Empty()
    {
        var v = new RequestPersonalDetailsChangeValidator();
        var result = v.Validate(new RequestPersonalDetailsChangeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Notes = string.Empty
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestPersonalDetailsChangeRequest.Notes));
    }

    [Fact]
    public void Validate_Fails_When_Notes_Is_Whitespace()
    {
        var v = new RequestPersonalDetailsChangeValidator();
        var result = v.Validate(new RequestPersonalDetailsChangeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Notes = "   "
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestPersonalDetailsChangeRequest.Notes));
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_2000_Characters()
    {
        var v = new RequestPersonalDetailsChangeValidator();
        var result = v.Validate(new RequestPersonalDetailsChangeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Notes = new string('N', 2001)
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestPersonalDetailsChangeRequest.Notes));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new RequestPersonalDetailsChangeValidator();
        var result = v.Validate(new RequestPersonalDetailsChangeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Notes = "Please update my date of birth to 1990-06-15."
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Notes_Is_At_Max_Length()
    {
        var v = new RequestPersonalDetailsChangeValidator();
        var result = v.Validate(new RequestPersonalDetailsChangeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Notes = new string('N', 2000)
        });
        Assert.True(result.IsValid);
    }
}

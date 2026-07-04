using HR.Modules.Employees.Features.DeactivateEmploymentType;

namespace HR.Modules.Employees.Tests;

public class DeactivateEmploymentTypeValidatorTests
{
    private static readonly DeactivateEmploymentTypeValidator Validator = new();

    private static DeactivateEmploymentTypeRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateEmploymentTypeRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateEmploymentTypeRequest.Id));
    }
}

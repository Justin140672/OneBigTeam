using HR.Modules.Employees.Features.UpdateEmploymentType;

namespace HR.Modules.Employees.Tests;

public class UpdateEmploymentTypeValidatorTests
{
    private static readonly UpdateEmploymentTypeValidator Validator = new();

    private static UpdateEmploymentTypeRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        Name = "Full Time",
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentTypeRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentTypeRequest.Id));
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Name = "" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentTypeRequest.Name));
    }

    [Fact]
    public void Validate_NameTooLong_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Name = new string('x', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentTypeRequest.Name));
    }

    [Fact]
    public void Validate_NameAtMaxLength_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { Name = new string('x', 100) }).IsValid);
    }

    [Fact]
    public void Validate_DescriptionTooLong_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Description = new string('x', 501) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentTypeRequest.Description));
    }

    [Fact]
    public void Validate_Passes_When_Description_Is_Null()
    {
        Assert.True(Validator.Validate(ValidRequest() with { Description = null }).IsValid);
    }
}

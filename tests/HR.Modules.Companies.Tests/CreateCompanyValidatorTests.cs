using HR.Modules.Companies.Features.CreateCompany;

namespace HR.Modules.Companies.Tests;

public class CreateCompanyValidatorTests
{
    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(new CreateCompanyRequest { Name = string.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateCompanyRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Slug_Format_Is_Invalid()
    {
        var validator = new CreateCompanyValidator();

        var result = validator.Validate(new CreateCompanyRequest
        {
            Name = "Acme Corp",
            Slug = "ACME corp"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateCompanyRequest.Slug));
    }
}

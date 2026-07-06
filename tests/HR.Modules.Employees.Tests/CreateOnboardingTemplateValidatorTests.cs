using HR.Modules.Employees.Features.CreateOnboardingTemplate;

namespace HR.Modules.Employees.Tests;

public class CreateOnboardingTemplateValidatorTests
{
    private readonly CreateOnboardingTemplateValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_For_Valid_Request()
    {
        var result = _validator.Validate(new CreateOnboardingTemplateRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Standard Onboarding",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new CreateOnboardingTemplateRequest
        {
            CompanyId = Guid.Empty,
            Name = "Standard Onboarding",
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = _validator.Validate(new CreateOnboardingTemplateRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "",
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_MaxLength()
    {
        var result = _validator.Validate(new CreateOnboardingTemplateRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = new string('a', 201),
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_MaxLength()
    {
        var result = _validator.Validate(new CreateOnboardingTemplateRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Standard Onboarding",
            Description = new string('a', 2001),
        });

        Assert.False(result.IsValid);
    }
}

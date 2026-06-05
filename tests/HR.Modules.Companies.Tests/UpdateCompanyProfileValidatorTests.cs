using HR.Modules.Companies.Features.UpdateCompanyProfile;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanyProfileValidatorTests
{
    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var validator = new UpdateCompanyProfileValidator();

        var result = validator.Validate(new UpdateCompanyProfileRequest
        {
            Id = Guid.Empty,
            Name = "Acme Corporation"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateCompanyProfileRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new UpdateCompanyProfileValidator();

        var result = validator.Validate(new UpdateCompanyProfileRequest
        {
            Id = Guid.NewGuid(),
            Name = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateCompanyProfileRequest.Name));
    }
}
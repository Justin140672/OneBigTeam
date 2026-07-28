using HR.Modules.Recruitment.Features.CreateExternalRecruiter;

namespace HR.Modules.Recruitment.Tests;

public class CreateExternalRecruiterValidatorTests
{
    private readonly CreateExternalRecruiterValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new CreateExternalRecruiterRequest(
            Guid.NewGuid(), "Acme Recruiting", "Jane Smith", "jane@acme.com", "01234", "https://acme.com", "Notes"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AgencyName_Is_Empty()
    {
        var result = _validator.Validate(new CreateExternalRecruiterRequest(
            Guid.NewGuid(), "", null, null, null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateExternalRecruiterRequest.AgencyName));
    }

    [Fact]
    public void Validate_Fails_When_AgencyName_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateExternalRecruiterRequest(
            Guid.NewGuid(), new string('A', 201), null, null, null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateExternalRecruiterRequest.AgencyName));
    }

    [Fact]
    public void Validate_Fails_When_ContactEmail_Is_Invalid_Format()
    {
        var result = _validator.Validate(new CreateExternalRecruiterRequest(
            Guid.NewGuid(), "Acme Recruiting", null, "not-an-email", null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateExternalRecruiterRequest.ContactEmail));
    }

    [Fact]
    public void Validate_Passes_When_ContactEmail_Is_Null()
    {
        var result = _validator.Validate(new CreateExternalRecruiterRequest(
            Guid.NewGuid(), "Acme Recruiting", null, null, null, null, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new CreateExternalRecruiterRequest(
            Guid.Empty, "Acme Recruiting", null, null, null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateExternalRecruiterRequest.CompanyId));
    }
}

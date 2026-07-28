using HR.Modules.Recruitment.Features.UpdateExternalRecruiter;

namespace HR.Modules.Recruitment.Tests;

public class UpdateExternalRecruiterValidatorTests
{
    private readonly UpdateExternalRecruiterValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new UpdateExternalRecruiterRequest(
            Guid.NewGuid(), Guid.NewGuid(), "Acme Recruiting", null, null, null, null, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AgencyName_Is_Empty()
    {
        var result = _validator.Validate(new UpdateExternalRecruiterRequest(
            Guid.NewGuid(), Guid.NewGuid(), "", null, null, null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateExternalRecruiterRequest.AgencyName));
    }

    [Fact]
    public void Validate_Fails_When_ContactEmail_Is_Invalid_Format()
    {
        var result = _validator.Validate(new UpdateExternalRecruiterRequest(
            Guid.NewGuid(), Guid.NewGuid(), "Acme Recruiting", null, "not-an-email", null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateExternalRecruiterRequest.ContactEmail));
    }

    [Fact]
    public void Validate_Fails_When_ExternalRecruiterId_Is_Empty()
    {
        var result = _validator.Validate(new UpdateExternalRecruiterRequest(
            Guid.NewGuid(), Guid.Empty, "Acme Recruiting", null, null, null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateExternalRecruiterRequest.ExternalRecruiterId));
    }
}

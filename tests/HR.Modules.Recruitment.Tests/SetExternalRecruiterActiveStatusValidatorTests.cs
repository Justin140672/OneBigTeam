using HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;

namespace HR.Modules.Recruitment.Tests;

public class SetExternalRecruiterActiveStatusValidatorTests
{
    private readonly SetExternalRecruiterActiveStatusValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new SetExternalRecruiterActiveStatusRequest(Guid.NewGuid(), Guid.NewGuid(), false));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new SetExternalRecruiterActiveStatusRequest(Guid.Empty, Guid.NewGuid(), false));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetExternalRecruiterActiveStatusRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_ExternalRecruiterId_Is_Empty()
    {
        var result = _validator.Validate(new SetExternalRecruiterActiveStatusRequest(Guid.NewGuid(), Guid.Empty, false));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetExternalRecruiterActiveStatusRequest.ExternalRecruiterId));
    }
}

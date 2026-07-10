using HR.Modules.Employees.Features.ListOnboardingTemplatesForPositionProfile;

namespace HR.Modules.Employees.Tests;

public class ListOnboardingTemplatesForPositionProfileValidatorTests
{
    private static readonly ListOnboardingTemplatesForPositionProfileValidator Validator = new();

    private static ListOnboardingTemplatesForPositionProfileRequest ValidRequest() => new(
        Guid.NewGuid(),
        Guid.NewGuid());

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListOnboardingTemplatesForPositionProfileRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyPositionProfileId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { PositionProfileId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListOnboardingTemplatesForPositionProfileRequest.PositionProfileId));
    }
}

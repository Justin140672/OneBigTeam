using HR.Modules.Recruitment.Features.GetApplicationsByStatus;

namespace HR.Modules.Recruitment.Tests;

public class GetApplicationsByStatusValidatorTests
{
    private readonly GetApplicationsByStatusValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetApplicationsByStatusRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetApplicationsByStatusRequest(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetApplicationsByStatusRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_StageId_Is_Empty()
    {
        var result = _validator.Validate(new GetApplicationsByStatusRequest(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetApplicationsByStatusRequest.StageId));
    }
}

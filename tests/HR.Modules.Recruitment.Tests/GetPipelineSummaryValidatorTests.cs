using HR.Modules.Recruitment.Features.GetPipelineSummary;

namespace HR.Modules.Recruitment.Tests;

public class GetPipelineSummaryValidatorTests
{
    private readonly GetPipelineSummaryValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetPipelineSummaryRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPipelineSummaryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetPipelineSummaryRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}

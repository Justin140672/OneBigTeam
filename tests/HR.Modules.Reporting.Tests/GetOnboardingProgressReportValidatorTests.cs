using HR.Modules.Reporting.Features.GetOnboardingProgressReport;

namespace HR.Modules.Reporting.Tests;

public class GetOnboardingProgressReportValidatorTests
{
    private readonly GetOnboardingProgressReportValidator _validator = new();

    [Fact]
    public void Should_Not_Have_Error_For_Valid_Request()
    {
        var result = _validator.Validate(new GetOnboardingProgressReportRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetOnboardingProgressReportRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetOnboardingProgressReportRequest.CompanyId));
    }
}

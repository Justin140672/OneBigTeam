using HR.Modules.Reporting.Features.GetRecruitmentPipelineSummaryReport;

namespace HR.Modules.Reporting.Tests;

public class GetRecruitmentPipelineSummaryReportValidatorTests
{
    private readonly GetRecruitmentPipelineSummaryReportValidator _validator = new();

    private static GetRecruitmentPipelineSummaryReportRequest ValidRequest() => new(Guid.NewGuid());

    [Fact]
    public void Should_Not_Have_Error_For_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_CompanyId_Is_Empty()
    {
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetRecruitmentPipelineSummaryReportRequest.CompanyId));
    }

    [Fact]
    public void Should_Not_Have_Error_When_IncludeClosed_Is_True()
    {
        var request = ValidRequest() with { IncludeClosed = true };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

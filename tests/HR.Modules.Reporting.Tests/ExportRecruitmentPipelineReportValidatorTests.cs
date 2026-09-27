using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportRecruitmentPipelineReport;
using HR.Modules.Reporting.Features.GetRecruitmentPipelineReport;

namespace HR.Modules.Reporting.Tests;

public class ExportRecruitmentPipelineReportValidatorTests
{
    private readonly ExportRecruitmentPipelineReportValidator _validator = new();

    private static ExportRecruitmentPipelineReportRequest ValidRequest() => new(Guid.NewGuid());

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportRecruitmentPipelineReportRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_GroupBy_Is_Invalid()
    {
        var request = ValidRequest() with { GroupBy = (RecruitmentPipelineGroupBy)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportRecruitmentPipelineReportRequest.GroupBy));
    }

    [Fact]
    public void Should_Have_Error_When_Format_Is_Invalid()
    {
        var request = ValidRequest() with { Format = (ReportExportFormat)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportRecruitmentPipelineReportRequest.Format));
    }

    [Fact]
    public void Should_Have_Error_When_EndDate_Is_Before_StartDate()
    {
        var request = ValidRequest() with
        {
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 5, 1),
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportRecruitmentPipelineReportRequest.EndDate));
    }

    // Internal recruitment Ticket 6: the optional isInternal filter is unconstrained — null (all),
    // true (internal only) and false (external only) are all valid.
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void Should_Not_Have_Error_For_Any_IsInternal_Value(bool? isInternal)
    {
        var result = _validator.Validate(ValidRequest() with { IsInternal = isInternal });

        Assert.True(result.IsValid);
    }
}

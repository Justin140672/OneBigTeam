using HR.Modules.Reporting.Features.SaveReportView;

namespace HR.Modules.Reporting.Tests;

public class SaveReportViewValidatorTests
{
    private readonly SaveReportViewValidator _validator = new();

    private static SaveReportViewRequest ValidRequest() =>
        new(Guid.NewGuid(), "employee-directory", "My View", "{}", null);

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_ReportId_Is_Empty()
    {
        var request = ValidRequest() with { ReportId = string.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.ReportId));
    }

    [Fact]
    public void Should_Have_Error_When_ReportId_Exceeds_MaxLength()
    {
        var request = ValidRequest() with { ReportId = new string('a', 201) };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.ReportId));
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Empty()
    {
        var request = ValidRequest() with { Name = string.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.Name));
    }

    [Fact]
    public void Should_Have_Error_When_Name_Exceeds_MaxLength()
    {
        var request = ValidRequest() with { Name = new string('a', 201) };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.Name));
    }

    [Fact]
    public void Should_Not_Have_Error_When_Name_Is_At_MaxLength()
    {
        var request = ValidRequest() with { Name = new string('a', 200) };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Not_Have_Error_When_ReportId_Is_At_MaxLength()
    {
        var request = ValidRequest() with { ReportId = new string('a', 200) };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_ReportId_Is_Whitespace()
    {
        var request = ValidRequest() with { ReportId = "   " };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.ReportId));
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Whitespace()
    {
        var request = ValidRequest() with { Name = "   " };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.Name));
    }

    [Fact]
    public void Should_Have_Error_When_FilterCriteriaJson_Is_Whitespace()
    {
        var request = ValidRequest() with { FilterCriteriaJson = "   " };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.FilterCriteriaJson));
    }

    [Fact]
    public void Should_Have_Error_When_FilterCriteriaJson_Is_Empty()
    {
        var request = ValidRequest() with { FilterCriteriaJson = string.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveReportViewRequest.FilterCriteriaJson));
    }

    [Fact]
    public void Should_Not_Have_Error_When_IsDefault_Is_Null()
    {
        var request = ValidRequest() with { IsDefault = null };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

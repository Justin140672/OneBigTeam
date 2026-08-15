using HR.Modules.Reporting.Features.AddReportFavourite;

namespace HR.Modules.Reporting.Tests;

public class AddReportFavouriteValidatorTests
{
    private readonly AddReportFavouriteValidator _validator = new();

    private static AddReportFavouriteRequest ValidRequest() => new(Guid.NewGuid(), "employee-directory");

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddReportFavouriteRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_ReportId_Is_Empty()
    {
        var request = ValidRequest() with { ReportId = string.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddReportFavouriteRequest.ReportId));
    }

    [Fact]
    public void Should_Have_Error_When_ReportId_Is_Whitespace()
    {
        var request = ValidRequest() with { ReportId = "   " };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddReportFavouriteRequest.ReportId));
    }

    [Fact]
    public void Should_Have_Error_When_ReportId_Exceeds_MaxLength()
    {
        var request = ValidRequest() with { ReportId = new string('a', 201) };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddReportFavouriteRequest.ReportId));
    }

    [Fact]
    public void Should_Not_Have_Error_When_ReportId_Is_At_MaxLength()
    {
        var request = ValidRequest() with { ReportId = new string('a', 200) };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

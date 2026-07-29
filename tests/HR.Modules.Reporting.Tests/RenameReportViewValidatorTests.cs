using HR.Modules.Reporting.Features.RenameReportView;

namespace HR.Modules.Reporting.Tests;

public class RenameReportViewValidatorTests
{
    private readonly RenameReportViewValidator _validator = new();

    private static RenameReportViewRequest ValidRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "New Name");

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RenameReportViewRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_ViewId_Is_Empty()
    {
        var request = ValidRequest() with { ViewId = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RenameReportViewRequest.ViewId));
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Empty()
    {
        var request = ValidRequest() with { Name = string.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RenameReportViewRequest.Name));
    }

    [Fact]
    public void Should_Have_Error_When_Name_Exceeds_MaxLength()
    {
        var request = ValidRequest() with { Name = new string('a', 201) };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RenameReportViewRequest.Name));
    }
}

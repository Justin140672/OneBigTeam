using HR.Modules.Reporting.Features.DeleteReportView;

namespace HR.Modules.Reporting.Tests;

public class DeleteReportViewValidatorTests
{
    private readonly DeleteReportViewValidator _validator = new();

    private static DeleteReportViewRequest ValidRequest() => new(Guid.NewGuid(), Guid.NewGuid());

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeleteReportViewRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_ViewId_Is_Empty()
    {
        var request = ValidRequest() with { ViewId = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeleteReportViewRequest.ViewId));
    }
}

using HR.Modules.Reporting.Features.GetReportCatalog;

namespace HR.Modules.Reporting.Tests;

public class GetReportCatalogValidatorTests
{
    private readonly GetReportCatalogValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_CompanyId_Is_Empty()
    {
        var request = new GetReportCatalogRequest(Guid.Empty);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetReportCatalogRequest.CompanyId));
    }

    [Fact]
    public void Should_Not_Have_Error_When_CompanyId_Is_Provided()
    {
        var request = new GetReportCatalogRequest(Guid.NewGuid());

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Features.ExportAccessReview;

namespace HR.Modules.Identity.Tests;

public class ExportAccessReviewValidatorTests
{
    private static ExportAccessReviewRequest ValidRequest() => new() { CompanyId = Guid.NewGuid() };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new ExportAccessReviewValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new ExportAccessReviewValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportAccessReviewRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Format_Is_Not_A_Defined_Enum_Value()
    {
        var validator = new ExportAccessReviewValidator();
        var request = ValidRequest() with { Format = (ReportExportFormat)999 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportAccessReviewRequest.Format));
    }

    [Theory]
    [InlineData(ReportExportFormat.Csv)]
    [InlineData(ReportExportFormat.Excel)]
    [InlineData(ReportExportFormat.Pdf)]
    public void Validate_Passes_For_Every_Defined_Format_Value(ReportExportFormat format)
    {
        var validator = new ExportAccessReviewValidator();
        var request = ValidRequest() with { Format = format };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

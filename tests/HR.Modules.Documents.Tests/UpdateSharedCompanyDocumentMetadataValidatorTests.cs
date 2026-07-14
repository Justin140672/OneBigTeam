using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;

namespace HR.Modules.Documents.Tests;

public class UpdateSharedCompanyDocumentMetadataValidatorTests
{
    private static readonly UpdateSharedCompanyDocumentMetadataValidator Validator = new();

    private static UpdateSharedCompanyDocumentMetadataRequest BuildRequest(
        SharedCompanyDocumentReviewFrequency reviewFrequency = SharedCompanyDocumentReviewFrequency.None,
        int? customReviewFrequencyMonths = null) =>
        new()
        {
            CompanyId                   = Guid.NewGuid(),
            DocumentId                  = Guid.NewGuid(),
            Title                       = "Remote Working Policy",
            CategoryId                  = Guid.NewGuid(),
            ReviewFrequency             = reviewFrequency,
            CustomReviewFrequencyMonths = customReviewFrequencyMonths,
        };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(BuildRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Custom_ReviewFrequency_With_Null_CustomReviewFrequencyMonths_Fails()
    {
        var result = Validator.Validate(BuildRequest(
            reviewFrequency: SharedCompanyDocumentReviewFrequency.Custom,
            customReviewFrequencyMonths: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSharedCompanyDocumentMetadataRequest.CustomReviewFrequencyMonths));
    }

    [Fact]
    public void Validate_Custom_ReviewFrequency_With_Zero_CustomReviewFrequencyMonths_Fails()
    {
        var result = Validator.Validate(BuildRequest(
            reviewFrequency: SharedCompanyDocumentReviewFrequency.Custom,
            customReviewFrequencyMonths: 0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSharedCompanyDocumentMetadataRequest.CustomReviewFrequencyMonths));
    }

    [Fact]
    public void Validate_Custom_ReviewFrequency_With_Negative_CustomReviewFrequencyMonths_Fails()
    {
        var result = Validator.Validate(BuildRequest(
            reviewFrequency: SharedCompanyDocumentReviewFrequency.Custom,
            customReviewFrequencyMonths: -1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSharedCompanyDocumentMetadataRequest.CustomReviewFrequencyMonths));
    }

    [Fact]
    public void Validate_Custom_ReviewFrequency_With_Positive_CustomReviewFrequencyMonths_Passes()
    {
        var result = Validator.Validate(BuildRequest(
            reviewFrequency: SharedCompanyDocumentReviewFrequency.Custom,
            customReviewFrequencyMonths: 6));

        Assert.True(result.IsValid);
    }

    // InlineData can't reference the internal SharedCompanyDocumentReviewFrequency enum directly on
    // a public Theory method, so the non-Custom values are passed as their underlying int and cast
    // back inside the method body.
    [Theory]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.None)]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.Monthly)]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.Quarterly)]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.SixMonthly)]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.Yearly)]
    public void Validate_NonCustom_ReviewFrequency_Passes_Regardless_Of_CustomReviewFrequencyMonths_Being_Null(int reviewFrequencyValue)
    {
        var reviewFrequency = (SharedCompanyDocumentReviewFrequency)reviewFrequencyValue;

        var result = Validator.Validate(BuildRequest(reviewFrequency: reviewFrequency, customReviewFrequencyMonths: null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.None)]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.Monthly)]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.Quarterly)]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.SixMonthly)]
    [InlineData((int)SharedCompanyDocumentReviewFrequency.Yearly)]
    public void Validate_NonCustom_ReviewFrequency_Passes_Regardless_Of_CustomReviewFrequencyMonths_Having_A_Value(int reviewFrequencyValue)
    {
        var reviewFrequency = (SharedCompanyDocumentReviewFrequency)reviewFrequencyValue;

        var result = Validator.Validate(BuildRequest(reviewFrequency: reviewFrequency, customReviewFrequencyMonths: 6));

        Assert.True(result.IsValid);
    }
}

using HR.Modules.Documents.Features.ListSharedCompanyDocumentsDueForReview;

namespace HR.Modules.Documents.Tests;

public class ListSharedCompanyDocumentsDueForReviewValidatorTests
{
    private static readonly ListSharedCompanyDocumentsDueForReviewValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new ListSharedCompanyDocumentsDueForReviewRequest(Guid.NewGuid()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new ListSharedCompanyDocumentsDueForReviewRequest(Guid.Empty));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListSharedCompanyDocumentsDueForReviewRequest.CompanyId));
    }
}

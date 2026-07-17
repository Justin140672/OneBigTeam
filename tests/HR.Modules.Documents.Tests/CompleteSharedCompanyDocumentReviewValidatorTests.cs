using HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview;

namespace HR.Modules.Documents.Tests;

public class CompleteSharedCompanyDocumentReviewValidatorTests
{
    private static readonly CompleteSharedCompanyDocumentReviewValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new CompleteSharedCompanyDocumentReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            ReviewNotes = "Reviewed against the latest legislation.",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new CompleteSharedCompanyDocumentReviewRequest
        {
            CompanyId = Guid.Empty,
            DocumentId = Guid.NewGuid(),
            ReviewNotes = "Reviewed.",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteSharedCompanyDocumentReviewRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyDocumentId_Fails()
    {
        var result = Validator.Validate(new CompleteSharedCompanyDocumentReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.Empty,
            ReviewNotes = "Reviewed.",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteSharedCompanyDocumentReviewRequest.DocumentId));
    }

    [Fact]
    public void Validate_EmptyReviewNotes_Fails()
    {
        var result = Validator.Validate(new CompleteSharedCompanyDocumentReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            ReviewNotes = string.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteSharedCompanyDocumentReviewRequest.ReviewNotes));
    }

    [Fact]
    public void Validate_WhitespaceOnlyReviewNotes_Fails()
    {
        var result = Validator.Validate(new CompleteSharedCompanyDocumentReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            ReviewNotes = "   ",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteSharedCompanyDocumentReviewRequest.ReviewNotes));
    }

    [Fact]
    public void Validate_ReviewNotesAtMaximumLength_Passes()
    {
        var result = Validator.Validate(new CompleteSharedCompanyDocumentReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            ReviewNotes = new string('a', 2000),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ReviewNotesExceedingMaximumLength_Fails()
    {
        var result = Validator.Validate(new CompleteSharedCompanyDocumentReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            ReviewNotes = new string('a', 2001),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteSharedCompanyDocumentReviewRequest.ReviewNotes));
    }
}

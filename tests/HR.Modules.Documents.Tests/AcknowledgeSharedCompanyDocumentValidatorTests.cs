using HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

namespace HR.Modules.Documents.Tests;

public class AcknowledgeSharedCompanyDocumentValidatorTests
{
    private static readonly AcknowledgeSharedCompanyDocumentValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new AcknowledgeSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Confirmed = true,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ConfirmedFalse_Fails_With_Expected_Message()
    {
        var result = Validator.Validate(new AcknowledgeSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Confirmed = false,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(AcknowledgeSharedCompanyDocumentRequest.Confirmed) &&
            e.ErrorMessage == "You must confirm that you have read and understood this document before it can be acknowledged.");
    }

    [Fact]
    public void Validate_ConfirmedTrue_Passes_That_Specific_Rule()
    {
        var result = Validator.Validate(new AcknowledgeSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Confirmed = true,
        });

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(AcknowledgeSharedCompanyDocumentRequest.Confirmed));
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new AcknowledgeSharedCompanyDocumentRequest
        {
            CompanyId = Guid.Empty,
            DocumentId = Guid.NewGuid(),
            Confirmed = true,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AcknowledgeSharedCompanyDocumentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyDocumentId_Fails()
    {
        var result = Validator.Validate(new AcknowledgeSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.Empty,
            Confirmed = true,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AcknowledgeSharedCompanyDocumentRequest.DocumentId));
    }
}

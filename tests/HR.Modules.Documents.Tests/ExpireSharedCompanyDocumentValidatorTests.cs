using HR.Modules.Documents.Features.ExpireSharedCompanyDocument;

namespace HR.Modules.Documents.Tests;

public class ExpireSharedCompanyDocumentValidatorTests
{
    private static readonly ExpireSharedCompanyDocumentValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new ExpireSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new ExpireSharedCompanyDocumentRequest
        {
            CompanyId = Guid.Empty,
            DocumentId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExpireSharedCompanyDocumentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyDocumentId_Fails()
    {
        var result = Validator.Validate(new ExpireSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExpireSharedCompanyDocumentRequest.DocumentId));
    }
}

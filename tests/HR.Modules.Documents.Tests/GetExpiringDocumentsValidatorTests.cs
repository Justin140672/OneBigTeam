using HR.Modules.Documents.Features.GetExpiringDocuments;

namespace HR.Modules.Documents.Tests;

public class GetExpiringDocumentsValidatorTests
{
    private static readonly GetExpiringDocumentsValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new GetExpiringDocumentsRequest { CompanyId = Guid.NewGuid() });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new GetExpiringDocumentsRequest { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetExpiringDocumentsRequest.CompanyId));
    }
}

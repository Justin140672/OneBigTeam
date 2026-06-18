using HR.Modules.Documents.Features.ListDocumentTypes;

namespace HR.Modules.Documents.Tests;

public class ListDocumentTypesValidatorTests
{
    private readonly ListDocumentTypesValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new ListDocumentTypesRequest
        {
            CompanyId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new ListDocumentTypesRequest
        {
            CompanyId = Guid.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDocumentTypesRequest.CompanyId));
    }
}

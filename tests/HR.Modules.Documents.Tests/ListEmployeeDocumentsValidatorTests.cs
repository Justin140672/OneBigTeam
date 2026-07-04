using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListEmployeeDocuments;

namespace HR.Modules.Documents.Tests;

public class ListEmployeeDocumentsValidatorTests
{
    private static readonly ListEmployeeDocumentsValidator Validator = new();

    private static ListEmployeeDocumentsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListEmployeeDocumentsRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListEmployeeDocumentsRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Passes_When_Status_Is_Null()
    {
        Assert.True(Validator.Validate(ValidRequest() with { Status = null }).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Status_Is_Specified()
    {
        Assert.True(Validator.Validate(ValidRequest() with { Status = DocumentStatus.Active }).IsValid);
    }
}

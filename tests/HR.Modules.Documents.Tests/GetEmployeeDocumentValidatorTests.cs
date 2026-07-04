using HR.Modules.Documents.Features.GetEmployeeDocument;

namespace HR.Modules.Documents.Tests;

public class GetEmployeeDocumentValidatorTests
{
    private static readonly GetEmployeeDocumentValidator Validator = new();

    private static GetEmployeeDocumentRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        EmployeeDocumentId = Guid.NewGuid(),
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDocumentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDocumentRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyEmployeeDocumentId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { EmployeeDocumentId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDocumentRequest.EmployeeDocumentId));
    }
}

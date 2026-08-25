using HR.Modules.Documents.Features.GetEmployeeDocumentVersionHistory;

namespace HR.Modules.Documents.Tests;

public class GetEmployeeDocumentVersionHistoryValidatorTests
{
    private static readonly GetEmployeeDocumentVersionHistoryValidator Validator = new();

    private static GetEmployeeDocumentVersionHistoryRequest ValidRequest() => new()
    {
        CompanyId          = Guid.NewGuid(),
        EmployeeId         = Guid.NewGuid(),
        EmployeeDocumentId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var request = ValidRequest() with { CompanyId = Guid.Empty };
        var result  = Validator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDocumentVersionHistoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var request = ValidRequest() with { EmployeeId = Guid.Empty };
        var result  = Validator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDocumentVersionHistoryRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyEmployeeDocumentId_Fails()
    {
        var request = ValidRequest() with { EmployeeDocumentId = Guid.Empty };
        var result  = Validator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeDocumentVersionHistoryRequest.EmployeeDocumentId));
    }
}

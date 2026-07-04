using HR.Modules.Documents.Features.RequestAdditionalEmployeeDocument;

namespace HR.Modules.Documents.Tests;

public class RequestAdditionalEmployeeDocumentValidatorTests
{
    private static readonly Validator Validator = new();

    private static RequestAdditionalEmployeeDocumentRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        DocumentTypeId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new RequestAdditionalEmployeeDocumentRequest
        {
            CompanyId = Guid.Empty,
            EmployeeId = request.EmployeeId,
            DocumentTypeId = request.DocumentTypeId,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestAdditionalEmployeeDocumentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new RequestAdditionalEmployeeDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = Guid.Empty,
            DocumentTypeId = request.DocumentTypeId,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestAdditionalEmployeeDocumentRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyDocumentTypeId_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new RequestAdditionalEmployeeDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentTypeId = Guid.Empty,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestAdditionalEmployeeDocumentRequest.DocumentTypeId));
    }

    [Fact]
    public void Validate_Passes_With_Optional_Fields_Populated()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new RequestAdditionalEmployeeDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentTypeId = request.DocumentTypeId,
            DueDate = DateOnly.FromDateTime(DateTime.Today).AddMonths(1),
            IsMandatory = true,
            Notes = "Please provide an updated copy.",
        });
        Assert.True(result.IsValid);
    }
}

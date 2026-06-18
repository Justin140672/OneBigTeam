using HR.Modules.Documents.Features.UploadEmployeeDocument;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Tests;

public class UploadEmployeeDocumentValidatorTests
{
    private static readonly UploadEmployeeDocumentValidator Validator = new();

    private static IFormFile FakeFile(string fileName = "contract.pdf") =>
        new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private static UploadEmployeeDocumentRequest ValidRequest() => new()
    {
        CompanyId      = Guid.NewGuid(),
        EmployeeId     = Guid.NewGuid(),
        DocumentTypeId = Guid.NewGuid(),
        Title          = "Employment Contract",
        File           = FakeFile(),
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
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentRequest
        {
            CompanyId      = Guid.Empty,
            EmployeeId     = request.EmployeeId,
            DocumentTypeId = request.DocumentTypeId,
            Title          = request.Title,
            File           = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentRequest
        {
            CompanyId      = request.CompanyId,
            EmployeeId     = Guid.Empty,
            DocumentTypeId = request.DocumentTypeId,
            Title          = request.Title,
            File           = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyDocumentTypeId_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentRequest
        {
            CompanyId      = request.CompanyId,
            EmployeeId     = request.EmployeeId,
            DocumentTypeId = Guid.Empty,
            Title          = request.Title,
            File           = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentRequest.DocumentTypeId));
    }

    [Fact]
    public void Validate_EmptyTitle_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentRequest
        {
            CompanyId      = request.CompanyId,
            EmployeeId     = request.EmployeeId,
            DocumentTypeId = request.DocumentTypeId,
            Title          = "",
            File           = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentRequest.Title));
    }

    [Fact]
    public void Validate_TitleTooLong_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentRequest
        {
            CompanyId      = request.CompanyId,
            EmployeeId     = request.EmployeeId,
            DocumentTypeId = request.DocumentTypeId,
            Title          = new string('x', 201),
            File           = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentRequest.Title));
    }

    [Fact]
    public void Validate_DescriptionTooLong_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentRequest
        {
            CompanyId      = request.CompanyId,
            EmployeeId     = request.EmployeeId,
            DocumentTypeId = request.DocumentTypeId,
            Title          = request.Title,
            Description    = new string('x', 1001),
            File           = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentRequest.Description));
    }

    [Fact]
    public void Validate_NullDescription_Passes()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentRequest
        {
            CompanyId      = request.CompanyId,
            EmployeeId     = request.EmployeeId,
            DocumentTypeId = request.DocumentTypeId,
            Title          = request.Title,
            Description    = null,
            File           = request.File,
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NullFile_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentRequest
        {
            CompanyId      = request.CompanyId,
            EmployeeId     = request.EmployeeId,
            DocumentTypeId = request.DocumentTypeId,
            Title          = request.Title,
            File           = null!,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentRequest.File));
    }
}

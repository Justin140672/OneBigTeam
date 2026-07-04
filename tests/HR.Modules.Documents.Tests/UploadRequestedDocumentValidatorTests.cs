using HR.Modules.Documents.Features.UploadRequestedDocument;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Tests;

public class UploadRequestedDocumentValidatorTests
{
    private static readonly UploadRequestedDocumentValidator Validator = new();

    private static IFormFile FakeFile(string fileName = "passport.pdf") =>
        new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private static UploadRequestedDocumentRequest ValidRequest() => new()
    {
        CompanyId         = Guid.NewGuid(),
        EmployeeId        = Guid.NewGuid(),
        DocumentRequestId = Guid.NewGuid(),
        Title             = "Passport Copy",
        File              = FakeFile(),
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
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = Guid.Empty,
            EmployeeId = request.EmployeeId,
            DocumentRequestId = request.DocumentRequestId,
            Title = request.Title,
            File = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadRequestedDocumentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = Guid.Empty,
            DocumentRequestId = request.DocumentRequestId,
            Title = request.Title,
            File = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadRequestedDocumentRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyDocumentRequestId_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentRequestId = Guid.Empty,
            Title = request.Title,
            File = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadRequestedDocumentRequest.DocumentRequestId));
    }

    [Fact]
    public void Validate_EmptyTitle_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentRequestId = request.DocumentRequestId,
            Title = "",
            File = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadRequestedDocumentRequest.Title));
    }

    [Fact]
    public void Validate_TitleTooLong_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentRequestId = request.DocumentRequestId,
            Title = new string('x', 201),
            File = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadRequestedDocumentRequest.Title));
    }

    [Fact]
    public void Validate_TitleAtMaxLength_Passes()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentRequestId = request.DocumentRequestId,
            Title = new string('x', 200),
            File = request.File,
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DescriptionTooLong_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentRequestId = request.DocumentRequestId,
            Title = request.Title,
            Description = new string('x', 1001),
            File = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadRequestedDocumentRequest.Description));
    }

    [Fact]
    public void Validate_NullDescription_Passes()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentRequestId = request.DocumentRequestId,
            Title = request.Title,
            Description = null,
            File = request.File,
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NullFile_Fails()
    {
        var request = ValidRequest();
        var result = Validator.Validate(new UploadRequestedDocumentRequest
        {
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            DocumentRequestId = request.DocumentRequestId,
            Title = request.Title,
            File = null!,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadRequestedDocumentRequest.File));
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A file must be provided.");
    }
}

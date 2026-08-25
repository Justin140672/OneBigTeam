using HR.Modules.Documents.Features.UploadEmployeeDocumentVersion;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Tests;

public class UploadEmployeeDocumentVersionValidatorTests
{
    private static readonly UploadEmployeeDocumentVersionValidator Validator = new();

    private static IFormFile FakeFile(string fileName = "contract.pdf") =>
        new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private static UploadEmployeeDocumentVersionRequest ValidRequest() => new()
    {
        CompanyId          = Guid.NewGuid(),
        EmployeeId         = Guid.NewGuid(),
        EmployeeDocumentId = Guid.NewGuid(),
        File               = FakeFile(),
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
        var result  = Validator.Validate(new UploadEmployeeDocumentVersionRequest
        {
            CompanyId          = Guid.Empty,
            EmployeeId         = request.EmployeeId,
            EmployeeDocumentId = request.EmployeeDocumentId,
            File               = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentVersionRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentVersionRequest
        {
            CompanyId          = request.CompanyId,
            EmployeeId         = Guid.Empty,
            EmployeeDocumentId = request.EmployeeDocumentId,
            File               = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentVersionRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyEmployeeDocumentId_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentVersionRequest
        {
            CompanyId          = request.CompanyId,
            EmployeeId         = request.EmployeeId,
            EmployeeDocumentId = Guid.Empty,
            File               = request.File,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentVersionRequest.EmployeeDocumentId));
    }

    [Fact]
    public void Validate_NullFile_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeDocumentVersionRequest
        {
            CompanyId          = request.CompanyId,
            EmployeeId         = request.EmployeeId,
            EmployeeDocumentId = request.EmployeeDocumentId,
            File               = null!,
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeDocumentVersionRequest.File));
    }
}

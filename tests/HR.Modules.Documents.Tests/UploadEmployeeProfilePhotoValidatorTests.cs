using HR.Modules.Documents.Features.UploadEmployeeProfilePhoto;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Tests;

public class UploadEmployeeProfilePhotoValidatorTests
{
    private static readonly UploadEmployeeProfilePhotoValidator Validator = new();

    private static IFormFile FakeFile(string fileName = "photo.png") =>
        new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "image/png",
        };

    private static UploadEmployeeProfilePhotoRequest ValidRequest() => new()
    {
        CompanyId  = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        File       = FakeFile(),
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
        var result  = Validator.Validate(new UploadEmployeeProfilePhotoRequest
        {
            CompanyId  = Guid.Empty,
            EmployeeId = request.EmployeeId,
            File       = request.File,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeProfilePhotoRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeProfilePhotoRequest
        {
            CompanyId  = request.CompanyId,
            EmployeeId = Guid.Empty,
            File       = request.File,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeProfilePhotoRequest.EmployeeId));
    }

    [Fact]
    public void Validate_NullFile_Fails()
    {
        var request = ValidRequest();
        var result  = Validator.Validate(new UploadEmployeeProfilePhotoRequest
        {
            CompanyId  = request.CompanyId,
            EmployeeId = request.EmployeeId,
            File       = null!,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeProfilePhotoRequest.File));
    }

    [Fact]
    public void Validate_EmptyCompanyId_And_EmployeeId_Reports_Both_Errors()
    {
        var result = Validator.Validate(new UploadEmployeeProfilePhotoRequest
        {
            CompanyId  = Guid.Empty,
            EmployeeId = Guid.Empty,
            File       = FakeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeProfilePhotoRequest.CompanyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadEmployeeProfilePhotoRequest.EmployeeId));
    }
}

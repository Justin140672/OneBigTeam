using HR.Modules.DataImport.Features.UploadImportFile;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.DataImport.Tests;

public class UploadImportFileValidatorTests
{
    private static readonly UploadImportFileValidator Validator = new();

    private static IFormFile FakeFile(string fileName = "employees.csv", string contentType = "text/csv") =>
        new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static UploadImportFileRequest ValidRequest() => new()
    {
        CompanyId  = Guid.NewGuid(),
        EntityType = "Employees",
        File       = FakeFile(),
    };

    [Fact]
    public void Valid_Request_Passes()
    {
        var result = Validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_CompanyId_Fails()
    {
        var request = ValidRequest();
        request = new UploadImportFileRequest
        {
            CompanyId  = Guid.Empty,
            EntityType = request.EntityType,
            File       = request.File,
        };

        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadImportFileRequest.CompanyId));
    }

    [Fact]
    public void Empty_EntityType_Fails()
    {
        var request = ValidRequest();
        request = new UploadImportFileRequest
        {
            CompanyId  = request.CompanyId,
            EntityType = string.Empty,
            File       = request.File,
        };

        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadImportFileRequest.EntityType));
    }

    [Fact]
    public void EntityType_Exceeding_MaxLength_Fails()
    {
        var request = ValidRequest();
        request = new UploadImportFileRequest
        {
            CompanyId  = request.CompanyId,
            EntityType = new string('E', 101),
            File       = request.File,
        };

        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadImportFileRequest.EntityType));
    }

    [Fact]
    public void EntityType_At_MaxLength_Passes()
    {
        var request = ValidRequest();
        request = new UploadImportFileRequest
        {
            CompanyId  = request.CompanyId,
            EntityType = new string('E', 100),
            File       = request.File,
        };

        var result = Validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Null_File_Fails()
    {
        var request = ValidRequest();
        request = new UploadImportFileRequest
        {
            CompanyId  = request.CompanyId,
            EntityType = request.EntityType,
            File       = null!,
        };

        var result = Validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadImportFileRequest.File));
    }
}

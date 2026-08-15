using HR.Modules.Recruitment.Features.UploadCandidateDocument;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Tests;

public class UploadCandidateDocumentValidatorTests
{
    private readonly UploadCandidateDocumentValidator _validator = new();

    private static IFormFile FakeFile() =>
        new FormFile(new MemoryStream(new byte[10]), 0, 10, "File", "resume.pdf")
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new UploadCandidateDocumentRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Title       = "Resume",
            File        = FakeFile(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CandidateId_Is_Empty()
    {
        var result = _validator.Validate(new UploadCandidateDocumentRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.Empty,
            Title       = "Resume",
            File        = FakeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCandidateDocumentRequest.CandidateId));
    }

    [Fact]
    public void Validate_Fails_When_Title_Is_Empty()
    {
        var result = _validator.Validate(new UploadCandidateDocumentRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Title       = string.Empty,
            File        = FakeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCandidateDocumentRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Title_Is_Whitespace_Only()
    {
        var result = _validator.Validate(new UploadCandidateDocumentRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Title       = "   ",
            File        = FakeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCandidateDocumentRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Title_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new UploadCandidateDocumentRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Title       = new string('A', 201),
            File        = FakeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCandidateDocumentRequest.Title));
    }

    [Fact]
    public void Validate_Passes_When_Title_Is_Exactly_Max_Length()
    {
        var result = _validator.Validate(new UploadCandidateDocumentRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Title       = new string('A', 200),
            File        = FakeFile(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_File_Is_Null()
    {
        var result = _validator.Validate(new UploadCandidateDocumentRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Title       = "Resume",
            File        = null!,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCandidateDocumentRequest.File));
    }
}

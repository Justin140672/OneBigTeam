using HR.Modules.Documents.Features.ArchiveSharedCompanyDocument;

namespace HR.Modules.Documents.Tests;

public class ArchiveSharedCompanyDocumentValidatorTests
{
    private static readonly ArchiveSharedCompanyDocumentValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new ArchiveSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Reason = "Superseded by a newer policy.",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new ArchiveSharedCompanyDocumentRequest
        {
            CompanyId = Guid.Empty,
            DocumentId = Guid.NewGuid(),
            Reason = "Reason",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ArchiveSharedCompanyDocumentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyDocumentId_Fails()
    {
        var result = Validator.Validate(new ArchiveSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.Empty,
            Reason = "Reason",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ArchiveSharedCompanyDocumentRequest.DocumentId));
    }

    [Fact]
    public void Validate_EmptyReason_Fails()
    {
        var result = Validator.Validate(new ArchiveSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Reason = string.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ArchiveSharedCompanyDocumentRequest.Reason));
    }

    [Fact]
    public void Validate_WhitespaceOnlyReason_Fails()
    {
        var result = Validator.Validate(new ArchiveSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Reason = "   ",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ArchiveSharedCompanyDocumentRequest.Reason));
    }

    [Fact]
    public void Validate_ReasonAtMaximumLength_Passes()
    {
        var result = Validator.Validate(new ArchiveSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Reason = new string('a', 500),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ReasonExceedingMaximumLength_Fails()
    {
        var result = Validator.Validate(new ArchiveSharedCompanyDocumentRequest
        {
            CompanyId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Reason = new string('a', 501),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ArchiveSharedCompanyDocumentRequest.Reason));
    }
}

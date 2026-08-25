using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.SearchEmployeeDocuments;

namespace HR.Modules.Documents.Tests;

public class SearchEmployeeDocumentsValidatorTests
{
    private static readonly SearchEmployeeDocumentsValidator Validator = new();

    private static SearchEmployeeDocumentsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        PageNumber = 1,
        PageSize = 20,
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }

    // ── CompanyId ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.CompanyId));
    }

    // ── PageNumber ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_PageNumber_Zero_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { PageNumber = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.PageNumber));
    }

    [Fact]
    public void Validate_PageNumber_Negative_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { PageNumber = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.PageNumber));
    }

    [Fact]
    public void Validate_PageNumber_One_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { PageNumber = 1 }).IsValid);
    }

    // ── PageSize ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_PageSize_Zero_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { PageSize = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.PageSize));
    }

    [Fact]
    public void Validate_PageSize_One_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { PageSize = 1 }).IsValid);
    }

    [Fact]
    public void Validate_PageSize_OneHundred_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { PageSize = 100 }).IsValid);
    }

    [Fact]
    public void Validate_PageSize_OneHundredAndOne_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { PageSize = 101 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.PageSize));
    }

    // ── SearchText ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_SearchText_Null_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { SearchText = null }).IsValid);
    }

    [Fact]
    public void Validate_SearchText_MaxLength_Passes()
    {
        var text = new string('a', 200);
        Assert.True(Validator.Validate(ValidRequest() with { SearchText = text }).IsValid);
    }

    [Fact]
    public void Validate_SearchText_ExceedsMaxLength_Fails()
    {
        var text = new string('a', 201);
        var result = Validator.Validate(ValidRequest() with { SearchText = text });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.SearchText));
    }

    // ── UploadedFrom / UploadedTo ──────────────────────────────────────────

    [Fact]
    public void Validate_UploadedTo_Before_UploadedFrom_Fails()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = from.AddDays(-1);
        var result = Validator.Validate(ValidRequest() with { UploadedFrom = from, UploadedTo = to });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.UploadedTo));
    }

    [Fact]
    public void Validate_UploadedTo_Equal_To_UploadedFrom_Passes()
    {
        var date = new DateOnly(2026, 6, 1);
        Assert.True(Validator.Validate(ValidRequest() with { UploadedFrom = date, UploadedTo = date }).IsValid);
    }

    [Fact]
    public void Validate_UploadedTo_After_UploadedFrom_Passes()
    {
        var from = new DateOnly(2026, 6, 1);
        Assert.True(Validator.Validate(ValidRequest() with { UploadedFrom = from, UploadedTo = from.AddDays(1) }).IsValid);
    }

    [Fact]
    public void Validate_UploadedFrom_Only_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { UploadedFrom = new DateOnly(2026, 6, 1) }).IsValid);
    }

    [Fact]
    public void Validate_UploadedTo_Only_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { UploadedTo = new DateOnly(2026, 6, 1) }).IsValid);
    }

    // ── ExpiresFrom / ExpiresTo ────────────────────────────────────────────

    [Fact]
    public void Validate_ExpiresTo_Before_ExpiresFrom_Fails()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = from.AddDays(-1);
        var result = Validator.Validate(ValidRequest() with { ExpiresFrom = from, ExpiresTo = to });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDocumentsRequest.ExpiresTo));
    }

    [Fact]
    public void Validate_ExpiresTo_Equal_To_ExpiresFrom_Passes()
    {
        var date = new DateOnly(2026, 6, 1);
        Assert.True(Validator.Validate(ValidRequest() with { ExpiresFrom = date, ExpiresTo = date }).IsValid);
    }

    [Fact]
    public void Validate_ExpiresTo_After_ExpiresFrom_Passes()
    {
        var from = new DateOnly(2026, 6, 1);
        Assert.True(Validator.Validate(ValidRequest() with { ExpiresFrom = from, ExpiresTo = from.AddDays(1) }).IsValid);
    }

    [Fact]
    public void Validate_ExpiresFrom_Only_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { ExpiresFrom = new DateOnly(2026, 6, 1) }).IsValid);
    }

    [Fact]
    public void Validate_ExpiresTo_Only_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { ExpiresTo = new DateOnly(2026, 6, 1) }).IsValid);
    }

    // ── Passthrough fields ─────────────────────────────────────────────────

    [Fact]
    public void Validate_Passes_When_Status_Is_Specified()
    {
        Assert.True(Validator.Validate(ValidRequest() with { Status = DocumentStatus.Active }).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_DocumentTypeId_And_EmployeeId_Specified()
    {
        Assert.True(Validator.Validate(ValidRequest() with
        {
            DocumentTypeId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
        }).IsValid);
    }
}

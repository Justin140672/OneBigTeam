using HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;

namespace HR.Modules.Employees.Tests;

public class AddRequiredDocumentToPositionProfileValidatorTests
{
    private static readonly AddRequiredDocumentValidator Validator = new();

    private static AddRequiredDocumentRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        PositionProfileId = Guid.NewGuid(),
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
        var result = Validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredDocumentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyPositionProfileId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { PositionProfileId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredDocumentRequest.PositionProfileId));
    }

    [Fact]
    public void Validate_EmptyDocumentTypeId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { DocumentTypeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredDocumentRequest.DocumentTypeId));
    }

    [Fact]
    public void Validate_NegativeDueDaysAfterStart_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { DueDaysAfterStart = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredDocumentRequest.DueDaysAfterStart));
    }

    [Fact]
    public void Validate_ZeroDueDaysAfterStart_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { DueDaysAfterStart = 0 }).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_DueDaysAfterStart_Is_Null()
    {
        Assert.True(Validator.Validate(ValidRequest() with { DueDaysAfterStart = null }).IsValid);
    }
}

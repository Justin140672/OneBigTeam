using HR.Modules.Documents.Features.CreateDocumentType;

namespace HR.Modules.Documents.Tests;

public class CreateDocumentTypeValidatorTests
{
    private readonly CreateDocumentTypeValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new CreateDocumentTypeRequest
        {
            CompanyId   = Guid.NewGuid(),
            Name        = "Contract",
            Description = "Employment contracts"
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_Without_Description()
    {
        var result = _validator.Validate(new CreateDocumentTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Name      = "Contract"
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new CreateDocumentTypeRequest
        {
            CompanyId = Guid.Empty,
            Name      = "Contract"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDocumentTypeRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = _validator.Validate(new CreateDocumentTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Name      = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDocumentTypeRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateDocumentTypeRequest
        {
            CompanyId = Guid.NewGuid(),
            Name      = new string('A', 201)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDocumentTypeRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateDocumentTypeRequest
        {
            CompanyId   = Guid.NewGuid(),
            Name        = "Contract",
            Description = new string('A', 1001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDocumentTypeRequest.Description));
    }
}

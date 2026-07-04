using HR.Modules.Assets.Features.CreateAssetAssignment;

namespace HR.Modules.Assets.Tests;

public class CreateAssetAssignmentValidatorTests
{
    private static readonly CreateAssetAssignmentValidator Validator = new();

    private static CreateAssetAssignmentRequest ValidRequest() => new()
    {
        CompanyId  = Guid.NewGuid(),
        AssetId    = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        AssignedBy = Guid.NewGuid(),
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
        var result = Validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetAssignmentRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyAssetId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { AssetId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetAssignmentRequest.AssetId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetAssignmentRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyAssignedBy_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { AssignedBy = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetAssignmentRequest.AssignedBy));
    }

    [Fact]
    public void Validate_NotesTooLong_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Notes = new string('x', 2001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetAssignmentRequest.Notes));
    }

    [Fact]
    public void Validate_NotesAtMaxLength_Passes()
    {
        var result = Validator.Validate(ValidRequest() with { Notes = new string('x', 2000) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NullNotes_Passes()
    {
        var result = Validator.Validate(ValidRequest() with { Notes = null });
        Assert.True(result.IsValid);
    }
}

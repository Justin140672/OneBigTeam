using HR.Modules.Assets.Features.UpdateAssetCategory;

namespace HR.Modules.Assets.Tests;

public class UpdateAssetCategoryValidatorTests
{
    private readonly UpdateAssetCategoryValidator _validator = new();

    private static UpdateAssetCategoryRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        Name = "Electronics",
        Description = "Electronic devices and accessories"
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Description_Is_Null()
    {
        Assert.True(_validator.Validate(Valid() with { Description = null }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateAssetCategoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateAssetCategoryRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateAssetCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('A', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateAssetCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_500_Characters()
    {
        var result = _validator.Validate(Valid() with { Description = new string('D', 501) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateAssetCategoryRequest.Description));
    }

    [Fact]
    public void Validate_Passes_When_Name_Is_Exactly_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('A', 100) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Description_Is_Exactly_500_Characters()
    {
        var result = _validator.Validate(Valid() with { Description = new string('D', 500) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Whitespace_Only()
    {
        var result = _validator.Validate(Valid() with { Name = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateAssetCategoryRequest.Name));
    }
}

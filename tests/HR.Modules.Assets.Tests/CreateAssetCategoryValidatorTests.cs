using HR.Modules.Assets.Features.CreateAssetCategory;

namespace HR.Modules.Assets.Tests;

public class CreateAssetCategoryValidatorTests
{
    private readonly CreateAssetCategoryValidator _validator = new();

    private static CreateAssetCategoryRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
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
        var result = _validator.Validate(Valid() with { Description = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetCategoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('A', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_500_Characters()
    {
        var result = _validator.Validate(Valid() with { Description = new string('D', 501) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetCategoryRequest.Description));
    }
}

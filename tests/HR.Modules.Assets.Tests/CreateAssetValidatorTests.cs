using HR.Modules.Assets.Features.CreateAsset;

namespace HR.Modules.Assets.Tests;

public class CreateAssetValidatorTests
{
    private readonly CreateAssetValidator _validator = new();

    private static CreateAssetRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        AssetNumber = "ASSET-001",
        CategoryId = Guid.NewGuid(),
        Name = "Laptop",
        Manufacturer = "Dell",
        Model = "XPS 15",
        SerialNumber = "SN123456",
        PurchaseDate = new DateOnly(2024, 1, 15),
        PurchasePrice = 1500.00m
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Optional_Fields_Are_Null()
    {
        var request = Valid() with
        {
            Manufacturer = null,
            Model = null,
            SerialNumber = null,
            PurchaseDate = null,
            PurchasePrice = null
        };
        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_AssetNumber_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { AssetNumber = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.AssetNumber));
    }

    [Fact]
    public void Validate_Fails_When_AssetNumber_Exceeds_50_Characters()
    {
        var result = _validator.Validate(Valid() with { AssetNumber = new string('A', 51) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.AssetNumber));
    }

    [Fact]
    public void Validate_Fails_When_CategoryId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CategoryId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.CategoryId));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_200_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('N', 201) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Manufacturer_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Manufacturer = new string('M', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.Manufacturer));
    }

    [Fact]
    public void Validate_Fails_When_Model_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Model = new string('X', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.Model));
    }

    [Fact]
    public void Validate_Fails_When_SerialNumber_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid() with { SerialNumber = new string('S', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.SerialNumber));
    }

    [Fact]
    public void Validate_Passes_When_AssetNumber_Is_Exactly_50_Characters()
    {
        var result = _validator.Validate(Valid() with { AssetNumber = new string('A', 50) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Name_Is_Exactly_200_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('N', 200) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Manufacturer_Is_Exactly_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Manufacturer = new string('M', 100) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Model_Is_Exactly_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Model = new string('X', 100) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_SerialNumber_Is_Exactly_100_Characters()
    {
        var result = _validator.Validate(Valid() with { SerialNumber = new string('S', 100) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AssetNumber_Is_Whitespace_Only()
    {
        var result = _validator.Validate(Valid() with { AssetNumber = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.AssetNumber));
    }

    [Fact]
    public void Validate_Passes_When_PurchasePrice_Is_Smallest_Positive_Value()
    {
        var result = _validator.Validate(Valid() with { PurchasePrice = 0.01m });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PurchasePrice_Is_Zero()
    {
        var result = _validator.Validate(Valid() with { PurchasePrice = 0m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.PurchasePrice));
    }

    [Fact]
    public void Validate_Fails_When_PurchasePrice_Is_Negative()
    {
        var result = _validator.Validate(Valid() with { PurchasePrice = -1m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateAssetRequest.PurchasePrice));
    }

    [Fact]
    public void Validate_Passes_When_PurchasePrice_Is_Null()
    {
        var result = _validator.Validate(Valid() with { PurchasePrice = null });
        Assert.True(result.IsValid);
    }
}

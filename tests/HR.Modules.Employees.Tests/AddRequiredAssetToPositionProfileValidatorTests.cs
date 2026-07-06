using HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;

namespace HR.Modules.Employees.Tests;

public class AddRequiredAssetToPositionProfileValidatorTests
{
    private static readonly AddRequiredAssetValidator Validator = new();

    private static AddRequiredAssetRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        PositionProfileId = Guid.NewGuid(),
        AssetCategoryId = Guid.NewGuid(),
        Quantity = 1,
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredAssetRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyPositionProfileId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { PositionProfileId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredAssetRequest.PositionProfileId));
    }

    [Fact]
    public void Validate_EmptyAssetCategoryId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { AssetCategoryId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredAssetRequest.AssetCategoryId));
    }

    [Fact]
    public void Validate_ZeroQuantity_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Quantity = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredAssetRequest.Quantity));
    }

    [Fact]
    public void Validate_NegativeQuantity_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Quantity = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRequiredAssetRequest.Quantity));
    }

    [Fact]
    public void Validate_QuantityOfOne_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { Quantity = 1 }).IsValid);
    }
}

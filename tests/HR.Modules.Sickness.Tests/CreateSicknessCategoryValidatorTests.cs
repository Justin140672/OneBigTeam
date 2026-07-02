using HR.Modules.Sickness.Features.CreateSicknessCategory;

namespace HR.Modules.Sickness.Tests;

public class CreateSicknessCategoryValidatorTests
{
    private readonly CreateSicknessCategoryValidator _validator = new();

    private static CreateSicknessCategoryRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        Name = "Cold",
        DisplayOrder = 1
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSicknessCategoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSicknessCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('A', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSicknessCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_DisplayOrder_Is_Zero()
    {
        var result = _validator.Validate(Valid() with { DisplayOrder = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSicknessCategoryRequest.DisplayOrder));
    }

    [Fact]
    public void Validate_Fails_When_DisplayOrder_Is_Negative()
    {
        var result = _validator.Validate(Valid() with { DisplayOrder = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateSicknessCategoryRequest.DisplayOrder));
    }
}

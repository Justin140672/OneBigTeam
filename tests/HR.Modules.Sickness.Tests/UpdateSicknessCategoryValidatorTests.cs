using HR.Modules.Sickness.Features.UpdateSicknessCategory;

namespace HR.Modules.Sickness.Tests;

public class UpdateSicknessCategoryValidatorTests
{
    private readonly UpdateSicknessCategoryValidator _validator = new();

    private static UpdateSicknessCategoryRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSicknessCategoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSicknessCategoryRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSicknessCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('A', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSicknessCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Succeeds_When_Name_Is_Exactly_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('A', 100) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Whitespace_Only()
    {
        var result = _validator.Validate(Valid() with { Name = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSicknessCategoryRequest.Name));
    }

    [Fact]
    public void Validate_Succeeds_When_DisplayOrder_Is_Zero()
    {
        var result = _validator.Validate(Valid() with { DisplayOrder = 0 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_DisplayOrder_Is_Negative()
    {
        var result = _validator.Validate(Valid() with { DisplayOrder = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSicknessCategoryRequest.DisplayOrder));
    }
}

using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.RecordMySickness;

namespace HR.Modules.Sickness.Tests;

public class RecordMySicknessValidatorTests
{
    private readonly RecordMySicknessValidator _validator = new();

    private static RecordMySicknessRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        StartDate = new DateOnly(2026, 7, 1),
        StartDayPart = SicknessDayPart.FullDay
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Notes_Is_Null()
    {
        Assert.True(_validator.Validate(Valid() with { Notes = null }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordMySicknessRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordMySicknessRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_CategoryId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CategoryId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordMySicknessRequest.CategoryId));
    }

    [Fact]
    public void Validate_Fails_When_StartDate_Is_Default()
    {
        var result = _validator.Validate(Valid() with { StartDate = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordMySicknessRequest.StartDate));
    }

    [Fact]
    public void Validate_Fails_When_StartDayPart_Is_Invalid()
    {
        var result = _validator.Validate(Valid() with { StartDayPart = (SicknessDayPart)99 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordMySicknessRequest.StartDayPart));
    }
}

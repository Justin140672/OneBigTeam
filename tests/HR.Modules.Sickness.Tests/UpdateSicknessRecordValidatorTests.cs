using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.UpdateSicknessRecord;

namespace HR.Modules.Sickness.Tests;

public class UpdateSicknessRecordValidatorTests
{
    private readonly UpdateSicknessRecordValidator _validator = new();

    private static UpdateSicknessRecordRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        StartDate = new DateOnly(2026, 7, 1),
        StartDayPart = SicknessDayPart.FullDay
    };

    [Fact]
    public void Validates_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Fails_When_CategoryId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CategoryId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "CategoryId");
    }

    [Fact]
    public void Fails_When_StartDate_Is_Default()
    {
        var result = _validator.Validate(ValidRequest() with { StartDate = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "StartDate");
    }

    [Fact]
    public void Fails_When_StartDayPart_Is_Invalid()
    {
        var result = _validator.Validate(ValidRequest() with { StartDayPart = (SicknessDayPart)99 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "StartDayPart");
    }
}

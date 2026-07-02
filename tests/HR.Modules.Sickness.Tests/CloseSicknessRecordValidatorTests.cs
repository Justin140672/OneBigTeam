using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.CloseSicknessRecord;

namespace HR.Modules.Sickness.Tests;

public class CloseSicknessRecordValidatorTests
{
    private readonly CloseSicknessRecordValidator _validator = new();

    private static CloseSicknessRecordRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        EndDate = new DateOnly(2026, 7, 3),
        EndDayPart = SicknessDayPart.FullDay
    };

    [Fact]
    public void Validates_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Fails_When_EndDate_Is_Default()
    {
        var result = _validator.Validate(ValidRequest() with { EndDate = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "EndDate");
    }

    [Fact]
    public void Fails_When_EndDayPart_Is_Invalid()
    {
        var result = _validator.Validate(ValidRequest() with { EndDayPart = (SicknessDayPart)99 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "EndDayPart");
    }

    [Fact]
    public void Succeeds_With_Optional_Fields_Null()
    {
        var result = _validator.Validate(ValidRequest() with { ReturnToWorkDate = null, Notes = null });
        Assert.True(result.IsValid);
    }
}

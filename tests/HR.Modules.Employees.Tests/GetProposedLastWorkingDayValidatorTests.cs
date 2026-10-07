using HR.Modules.Employees.Features.GetProposedLastWorkingDay;

namespace HR.Modules.Employees.Tests;

public class GetProposedLastWorkingDayValidatorTests
{
    private static GetProposedLastWorkingDayRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeavingDate = new DateOnly(2026, 10, 9)
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new GetProposedLastWorkingDayValidator().Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new GetProposedLastWorkingDayValidator().Validate(ValidRequest() with { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetProposedLastWorkingDayRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var result = new GetProposedLastWorkingDayValidator().Validate(ValidRequest() with { EmployeeId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetProposedLastWorkingDayRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeavingDate_Is_Default()
    {
        var result = new GetProposedLastWorkingDayValidator().Validate(ValidRequest() with { LeavingDate = default });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetProposedLastWorkingDayRequest.LeavingDate));
    }

    [Fact]
    public void Validate_Passes_For_Day_After_Default_LeavingDate()
    {
        var result = new GetProposedLastWorkingDayValidator().Validate(
            ValidRequest() with { LeavingDate = DateOnly.MinValue.AddDays(1) });

        Assert.True(result.IsValid);
    }
}

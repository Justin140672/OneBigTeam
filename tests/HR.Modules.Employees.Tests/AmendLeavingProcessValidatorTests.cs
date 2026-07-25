using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AmendLeavingProcess;

namespace HR.Modules.Employees.Tests;

public class AmendLeavingProcessValidatorTests
{
    private static AmendLeavingProcessRequest ValidRequest() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LeavingDate: new DateOnly(2026, 8, 1),
            LastWorkingDay: new DateOnly(2026, 7, 31),
            LeavingReason.Resignation);

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new AmendLeavingProcessValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new AmendLeavingProcessValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AmendLeavingProcessRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new AmendLeavingProcessValidator();
        var request = ValidRequest() with { EmployeeId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AmendLeavingProcessRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeavingDate_Is_Default()
    {
        var validator = new AmendLeavingProcessValidator();
        var request = ValidRequest() with { LeavingDate = default };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AmendLeavingProcessRequest.LeavingDate));
    }

    [Fact]
    public void Validate_Fails_When_LastWorkingDay_Is_Default()
    {
        var validator = new AmendLeavingProcessValidator();
        var request = ValidRequest() with { LastWorkingDay = default };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AmendLeavingProcessRequest.LastWorkingDay));
    }

    [Fact]
    public void Validate_Fails_When_LastWorkingDay_Is_After_LeavingDate()
    {
        var validator = new AmendLeavingProcessValidator();
        var request = ValidRequest() with
        {
            LeavingDate = new DateOnly(2026, 7, 31),
            LastWorkingDay = new DateOnly(2026, 8, 1)
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(AmendLeavingProcessRequest.LastWorkingDay)
            && e.ErrorMessage == "LastWorkingDay must be on or before LeavingDate.");
    }

    [Fact]
    public void Validate_Passes_When_LastWorkingDay_Equals_LeavingDate()
    {
        var validator = new AmendLeavingProcessValidator();
        var date = new DateOnly(2026, 8, 1);
        var request = ValidRequest() with { LeavingDate = date, LastWorkingDay = date };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LeavingReason_Is_Invalid_Enum_Value()
    {
        var validator = new AmendLeavingProcessValidator();
        var request = ValidRequest() with { LeavingReason = (LeavingReason)999 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AmendLeavingProcessRequest.LeavingReason));
    }

    // LeavingReason is internal, so the theory parameter must be a publicly-accessible type
    // (int) to avoid CS0051 — the enum value is cast back internally in the method body.
    [Theory]
    [InlineData((int)LeavingReason.Resignation)]
    [InlineData((int)LeavingReason.Redundancy)]
    [InlineData((int)LeavingReason.Dismissal)]
    [InlineData((int)LeavingReason.Retirement)]
    [InlineData((int)LeavingReason.EndOfContract)]
    [InlineData((int)LeavingReason.MutualAgreement)]
    [InlineData((int)LeavingReason.Other)]
    public void Validate_Passes_For_Every_Valid_LeavingReason(int reasonValue)
    {
        var reason = (LeavingReason)reasonValue;
        var validator = new AmendLeavingProcessValidator();
        var request = ValidRequest() with { LeavingReason = reason };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

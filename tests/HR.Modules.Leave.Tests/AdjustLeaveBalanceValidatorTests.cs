using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.AdjustLeaveBalance;

namespace HR.Modules.Leave.Tests;

public class AdjustLeaveBalanceValidatorTests
{
    private static AdjustLeaveBalanceRequest ValidRequest() => new(
        CompanyId: Guid.NewGuid(),
        EmployeeId: Guid.NewGuid(),
        LeaveTypeId: Guid.NewGuid(),
        AdjustmentValue: 7.5m,
        Reason: LeaveBalanceAdjustmentReason.Correction,
        Comments: "Correcting a data entry error",
        AllowNegativeOverride: false);

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdjustLeaveBalanceRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdjustLeaveBalanceRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeaveTypeId_Is_Empty()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { LeaveTypeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdjustLeaveBalanceRequest.LeaveTypeId));
    }

    [Fact]
    public void Validate_Fails_When_AdjustmentValue_Is_Zero()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { AdjustmentValue = 0m });
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(AdjustLeaveBalanceRequest.AdjustmentValue));
        Assert.Equal("Adjustment cannot be zero.", error.ErrorMessage);
    }

    [Fact]
    public void Validate_Passes_When_AdjustmentValue_Is_Negative()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { AdjustmentValue = -3.75m });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AdjustmentValue_Exceeds_Maximum()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { AdjustmentValue = 100_000m });
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(AdjustLeaveBalanceRequest.AdjustmentValue));
        Assert.Equal("Adjustment must be between -9,999.99 and 9,999.99.", error.ErrorMessage);
    }

    [Fact]
    public void Validate_Fails_When_AdjustmentValue_Is_Below_Minimum()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { AdjustmentValue = -100_000m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdjustLeaveBalanceRequest.AdjustmentValue));
    }

    [Fact]
    public void Validate_Passes_When_AdjustmentValue_Is_At_Maximum_Boundary()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { AdjustmentValue = 9999.99m });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_AdjustmentValue_Is_At_Minimum_Boundary()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { AdjustmentValue = -9999.99m });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Not_Defined_Enum_Value()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { Reason = (LeaveBalanceAdjustmentReason)999 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdjustLeaveBalanceRequest.Reason));
    }

    [Theory]
    [InlineData((int)LeaveBalanceAdjustmentReason.Correction)]
    [InlineData((int)LeaveBalanceAdjustmentReason.CarryOver)]
    [InlineData((int)LeaveBalanceAdjustmentReason.ManualAward)]
    [InlineData((int)LeaveBalanceAdjustmentReason.ManualDeduction)]
    [InlineData((int)LeaveBalanceAdjustmentReason.Other)]
    public void Validate_Passes_For_Every_Defined_Reason(int reasonValue)
    {
        var reason = (LeaveBalanceAdjustmentReason)reasonValue;
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { Reason = reason });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Comments_Exceeds_500_Characters()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { Comments = new string('C', 501) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdjustLeaveBalanceRequest.Comments));
    }

    [Fact]
    public void Validate_Passes_When_Comments_Is_At_Max_Length()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { Comments = new string('C', 500) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Comments_Is_Null()
    {
        var v = new AdjustLeaveBalanceValidator();
        var result = v.Validate(ValidRequest() with { Comments = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new AdjustLeaveBalanceValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }
}

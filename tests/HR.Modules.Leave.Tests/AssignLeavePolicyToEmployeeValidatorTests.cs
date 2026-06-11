using HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

namespace HR.Modules.Leave.Tests;

public class AssignLeavePolicyToEmployeeValidatorTests
{
    [Fact]
    public void Validate_Passes_With_Valid_Request()
    {
        var validator = new AssignLeavePolicyToEmployeeValidator();

        var result = validator.Validate(new AssignLeavePolicyToEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeavePolicyId = Guid.NewGuid(),
            EffectiveFrom = new DateOnly(2026, 7, 1)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new AssignLeavePolicyToEmployeeValidator();

        var result = validator.Validate(new AssignLeavePolicyToEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.Empty,
            LeavePolicyId = Guid.NewGuid(),
            EffectiveFrom = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignLeavePolicyToEmployeeRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeavePolicyId_Is_Empty()
    {
        var validator = new AssignLeavePolicyToEmployeeValidator();

        var result = validator.Validate(new AssignLeavePolicyToEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeavePolicyId = Guid.Empty,
            EffectiveFrom = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignLeavePolicyToEmployeeRequest.LeavePolicyId));
    }

    [Fact]
    public void Validate_Fails_When_EffectiveFrom_Is_Default()
    {
        var validator = new AssignLeavePolicyToEmployeeValidator();

        var result = validator.Validate(new AssignLeavePolicyToEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeavePolicyId = Guid.NewGuid(),
            EffectiveFrom = DateOnly.MinValue
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignLeavePolicyToEmployeeRequest.EffectiveFrom));
    }
}

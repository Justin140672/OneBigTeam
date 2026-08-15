using HR.Modules.Leave.Features.UpdateLeavePolicy;

namespace HR.Modules.Leave.Tests;

public class UpdateLeavePolicyValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            Name = "Updated Policy",
            Description = "An updated leave policy",
            CarryOverDays = 5,
            AllowNegativeBalance = false
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            Name = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeavePolicyRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_Max_Length()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            Name = new string('A', 201)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeavePolicyRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_CarryOverDays_Is_Negative()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            Name = "Policy",
            CarryOverDays = -1
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeavePolicyRequest.CarryOverDays));
    }

    [Fact]
    public void Validate_Fails_When_CarryOverDays_Exceeds_365()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            Name = "Policy",
            CarryOverDays = 366
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeavePolicyRequest.CarryOverDays));
    }

    [Fact]
    public void Validate_Passes_When_CarryOverDays_Is_Zero()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            Name = "Policy",
            CarryOverDays = 0
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_CarryOverDays_Is_Exactly_365()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            Name = "Policy",
            CarryOverDays = 365
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Name_Is_Exactly_200_Characters()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            Name = new string('A', 200)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PolicyId_Is_Empty()
    {
        var validator = new UpdateLeavePolicyValidator();

        var result = validator.Validate(new UpdateLeavePolicyRequest
        {
            CompanyId = Guid.NewGuid(),
            PolicyId = Guid.Empty,
            Name = "Policy"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeavePolicyRequest.PolicyId));
    }
}

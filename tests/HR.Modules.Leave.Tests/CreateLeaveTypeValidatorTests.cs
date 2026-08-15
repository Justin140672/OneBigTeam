using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.CreateLeaveType;

namespace HR.Modules.Leave.Tests;

public class CreateLeaveTypeValidatorTests
{
    private readonly CreateLeaveTypeValidator _validator = new();

    private static CreateLeaveTypeRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        Name = "Annual Leave",
        Code = "ANNUAL",
        DefaultEntitlementDays = 25,
        AccrualMethod = AccrualMethod.Monthly,
        Behaviour = LeaveTypeBehaviour.Standard
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateLeaveTypeRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateLeaveTypeRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('A', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateLeaveTypeRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Code_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Code = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateLeaveTypeRequest.Code));
    }

    [Fact]
    public void Validate_Fails_When_Code_Exceeds_20_Characters()
    {
        var result = _validator.Validate(Valid() with { Code = new string('A', 21) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateLeaveTypeRequest.Code));
    }

    [Fact]
    public void Validate_Fails_When_DefaultEntitlementDays_Is_Negative()
    {
        var result = _validator.Validate(Valid() with { DefaultEntitlementDays = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateLeaveTypeRequest.DefaultEntitlementDays));
    }

    [Fact]
    public void Validate_Passes_When_DefaultEntitlementDays_Is_Zero()
    {
        var result = _validator.Validate(Valid() with { DefaultEntitlementDays = 0 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Name_Is_Exactly_100_Characters()
    {
        var result = _validator.Validate(Valid() with { Name = new string('A', 100) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Code_Is_Exactly_20_Characters()
    {
        var result = _validator.Validate(Valid() with { Code = new string('A', 20) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Whitespace_Only()
    {
        var result = _validator.Validate(Valid() with { Name = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateLeaveTypeRequest.Name));
    }
}

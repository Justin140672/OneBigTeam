using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.UpdateLeaveType;

namespace HR.Modules.Leave.Tests;

public class UpdateLeaveTypeValidatorTests
{
    private static readonly UpdateLeaveTypeValidator Validator = new();

    private static UpdateLeaveTypeRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        Name = "Annual Leave",
        Code = "ANNUAL",
        DefaultEntitlementDays = 25,
        AccrualMethod = AccrualMethod.Monthly,
        Behaviour = LeaveTypeBehaviour.Standard,
    };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeaveTypeRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeaveTypeRequest.Id));
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Name = "" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeaveTypeRequest.Name));
    }

    [Fact]
    public void Validate_NameTooLong_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Name = new string('x', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeaveTypeRequest.Name));
    }

    [Fact]
    public void Validate_EmptyCode_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Code = "" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeaveTypeRequest.Code));
    }

    [Fact]
    public void Validate_CodeTooLong_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Code = new string('x', 21) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeaveTypeRequest.Code));
    }

    [Fact]
    public void Validate_NegativeDefaultEntitlementDays_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { DefaultEntitlementDays = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateLeaveTypeRequest.DefaultEntitlementDays));
    }

    [Fact]
    public void Validate_ZeroDefaultEntitlementDays_Passes()
    {
        Assert.True(Validator.Validate(ValidRequest() with { DefaultEntitlementDays = 0 }).IsValid);
    }
}

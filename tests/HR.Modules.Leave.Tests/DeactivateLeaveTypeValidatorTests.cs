using HR.Modules.Leave.Features.DeactivateLeaveType;

namespace HR.Modules.Leave.Tests;

public class DeactivateLeaveTypeValidatorTests
{
    private static readonly DeactivateLeaveTypeValidator Validator = new();

    private static DeactivateLeaveTypeRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateLeaveTypeRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyId_Fails()
    {
        var result = Validator.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateLeaveTypeRequest.Id));
    }
}

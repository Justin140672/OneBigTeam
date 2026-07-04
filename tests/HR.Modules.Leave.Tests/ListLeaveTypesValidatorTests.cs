using HR.Modules.Leave.Features.ListLeaveTypes;

namespace HR.Modules.Leave.Tests;

public class ListLeaveTypesValidatorTests
{
    private static readonly ListLeaveTypesValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new ListLeaveTypesRequest { CompanyId = Guid.NewGuid() });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new ListLeaveTypesRequest { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListLeaveTypesRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_When_IsActive_Is_Specified()
    {
        var result = Validator.Validate(new ListLeaveTypesRequest { CompanyId = Guid.NewGuid(), IsActive = false });
        Assert.True(result.IsValid);
    }
}

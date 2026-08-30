using HR.Modules.Identity.Features.ListInvitableEmployees;

namespace HR.Modules.Identity.Tests;

public class ListInvitableEmployeesValidatorTests
{
    private readonly ListInvitableEmployeesValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_CompanyId()
    {
        var result = _validator.Validate(new ListInvitableEmployeesRequest { CompanyId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new ListInvitableEmployeesRequest { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListInvitableEmployeesRequest.CompanyId));
    }
}

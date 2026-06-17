using HR.Modules.Employees.Features.ListPositionProfiles;

namespace HR.Modules.Employees.Tests;

public class ListPositionProfilesValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new ListPositionProfilesValidator();
        var result = v.Validate(new ListPositionProfilesRequest { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListPositionProfilesRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new ListPositionProfilesValidator();
        var result = v.Validate(new ListPositionProfilesRequest { CompanyId = Guid.NewGuid() });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_IncludeInactive_Is_True()
    {
        var v = new ListPositionProfilesValidator();
        var result = v.Validate(new ListPositionProfilesRequest
        {
            CompanyId = Guid.NewGuid(),
            IncludeInactive = true
        });
        Assert.True(result.IsValid);
    }
}

using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.AssignPlatformAdministratorRole;

namespace HR.Modules.Identity.Tests;

public class AssignPlatformAdministratorRoleValidatorTests
{
    private static AssignPlatformAdministratorRoleRequest ValidRequest() =>
        new(Guid.NewGuid(), PlatformAdministratorRole.PlatformOwner);

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new AssignPlatformAdministratorRoleValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var validator = new AssignPlatformAdministratorRoleValidator();
        var request = ValidRequest() with { Id = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignPlatformAdministratorRoleRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Role_Is_Not_A_Defined_Enum_Value()
    {
        var validator = new AssignPlatformAdministratorRoleValidator();
        var request = ValidRequest() with { Role = (PlatformAdministratorRole)999 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignPlatformAdministratorRoleRequest.Role));
    }
}

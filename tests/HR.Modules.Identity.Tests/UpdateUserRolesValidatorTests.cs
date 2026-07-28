using HR.Modules.Identity.Features.UpdateUserRoles;

namespace HR.Modules.Identity.Tests;

public class UpdateUserRolesValidatorTests
{
    private static UpdateUserRolesRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        RoleIds = [Guid.NewGuid()],
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new UpdateUserRolesValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new UpdateUserRolesValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRolesRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_UserId_Is_Empty()
    {
        var validator = new UpdateUserRolesValidator();
        var request = ValidRequest() with { UserId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRolesRequest.UserId));
    }

    [Fact]
    public void Validate_Fails_When_RoleIds_Is_Empty()
    {
        var validator = new UpdateUserRolesValidator();
        var request = ValidRequest() with { RoleIds = [] };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRolesRequest.RoleIds));
    }
}

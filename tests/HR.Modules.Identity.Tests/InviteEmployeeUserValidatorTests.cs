using HR.Modules.Identity.Features.InviteEmployeeUser;

namespace HR.Modules.Identity.Tests;

public class InviteEmployeeUserValidatorTests
{
    private static InviteEmployeeUserRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        Email = "user@example.com",
        RoleIds = [Guid.NewGuid()],
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new InviteEmployeeUserValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new InviteEmployeeUserValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(InviteEmployeeUserRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new InviteEmployeeUserValidator();
        var request = ValidRequest() with { EmployeeId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(InviteEmployeeUserRequest.EmployeeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_Fails_For_Invalid_Email(string email)
    {
        var validator = new InviteEmployeeUserValidator();
        var request = ValidRequest() with { Email = email };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(InviteEmployeeUserRequest.Email));
    }

    [Fact]
    public void Validate_Fails_When_RoleIds_Is_Empty()
    {
        var validator = new InviteEmployeeUserValidator();
        var request = ValidRequest() with { RoleIds = [] };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(InviteEmployeeUserRequest.RoleIds));
    }
}

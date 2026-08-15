using HR.Modules.Identity.Features.ResetPassword;

namespace HR.Modules.Identity.Tests;

public class ResetPasswordValidatorTests
{
    private static ResetPasswordRequest ValidRequest() => new("access-token", "P@ssw0rd123");

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new ResetPasswordValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AccessToken_Is_Empty()
    {
        var validator = new ResetPasswordValidator();
        var request = ValidRequest() with { AccessToken = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPasswordRequest.AccessToken));
    }

    [Fact]
    public void Validate_Fails_When_NewPassword_Is_Empty()
    {
        var validator = new ResetPasswordValidator();
        var request = ValidRequest() with { NewPassword = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPasswordRequest.NewPassword));
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("1234567")]
    public void Validate_Fails_When_NewPassword_Is_Too_Short(string password)
    {
        var validator = new ResetPasswordValidator();
        var request = ValidRequest() with { NewPassword = password };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPasswordRequest.NewPassword));
    }

    [Fact]
    public void Validate_Passes_When_NewPassword_Is_Exactly_MinLength()
    {
        var validator = new ResetPasswordValidator();
        var request = ValidRequest() with { NewPassword = "12345678" };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

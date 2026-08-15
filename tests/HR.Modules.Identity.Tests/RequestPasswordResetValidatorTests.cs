using HR.Modules.Identity.Features.RequestPasswordReset;

namespace HR.Modules.Identity.Tests;

public class RequestPasswordResetValidatorTests
{
    private static RequestPasswordResetRequest ValidRequest() => new("ada@example.com");

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new RequestPasswordResetValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Empty()
    {
        var validator = new RequestPasswordResetValidator();
        var request = ValidRequest() with { Email = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestPasswordResetRequest.Email));
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Invalid_Format()
    {
        var validator = new RequestPasswordResetValidator();
        var request = ValidRequest() with { Email = "not-an-email" };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestPasswordResetRequest.Email));
    }

    [Fact]
    public void Validate_Fails_When_Email_Exceeds_MaxLength()
    {
        var validator = new RequestPasswordResetValidator();
        var longLocalPart = new string('a', 250);
        var request = ValidRequest() with { Email = $"{longLocalPart}@example.com" };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RequestPasswordResetRequest.Email));
    }

    [Fact]
    public void Validate_Passes_When_Email_Is_Exactly_MaxLength()
    {
        var validator = new RequestPasswordResetValidator();
        // "a...@example.com" - 256 chars total.
        var localPart = new string('a', 256 - "@example.com".Length);
        var request = ValidRequest() with { Email = $"{localPart}@example.com" };

        Assert.Equal(256, request.Email.Length);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

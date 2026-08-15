using HR.Modules.Identity.Features.SignUp;

namespace HR.Modules.Identity.Tests;

public class SignUpValidatorTests
{
    private static SignUpRequest ValidRequest() => new(
        CompanyName: "Acme Corp",
        AdminFirstName: "Ada",
        AdminLastName: "Lovelace",
        AdminEmail: "ada@example.com",
        Password: "P@ssw0rd123");

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new SignUpValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyName_Is_Empty()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { CompanyName = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.CompanyName));
    }

    [Fact]
    public void Validate_Fails_When_CompanyName_Exceeds_MaxLength()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { CompanyName = new string('a', 201) };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.CompanyName));
    }

    [Fact]
    public void Validate_Fails_When_AdminFirstName_Is_Empty()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { AdminFirstName = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.AdminFirstName));
    }

    [Fact]
    public void Validate_Fails_When_AdminFirstName_Exceeds_MaxLength()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { AdminFirstName = new string('a', 101) };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.AdminFirstName));
    }

    [Fact]
    public void Validate_Fails_When_AdminLastName_Is_Empty()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { AdminLastName = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.AdminLastName));
    }

    [Fact]
    public void Validate_Fails_When_AdminLastName_Exceeds_MaxLength()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { AdminLastName = new string('a', 101) };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.AdminLastName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_Fails_For_Invalid_AdminEmail(string email)
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { AdminEmail = email };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.AdminEmail));
    }

    [Fact]
    public void Validate_Fails_When_AdminEmail_Exceeds_MaxLength()
    {
        var validator = new SignUpValidator();
        var longLocalPart = new string('a', 250);
        var request = ValidRequest() with { AdminEmail = $"{longLocalPart}@example.com" };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.AdminEmail));
    }

    [Fact]
    public void Validate_Fails_When_Password_Is_Empty()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { Password = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.Password));
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("1234567")]
    public void Validate_Fails_When_Password_Is_Too_Short(string password)
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { Password = password };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SignUpRequest.Password));
    }

    [Fact]
    public void Validate_Passes_When_Password_Is_Exactly_MinLength()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { Password = "12345678" };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_CompanyName_Is_Exactly_MaxLength()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { CompanyName = new string('a', 200) };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_AdminFirstName_Is_Exactly_MaxLength()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { AdminFirstName = new string('a', 100) };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_AdminLastName_Is_Exactly_MaxLength()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { AdminLastName = new string('a', 100) };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_AdminEmail_Is_Exactly_MaxLength()
    {
        var validator = new SignUpValidator();
        var localPart = new string('a', 256 - "@example.com".Length);
        var request = ValidRequest() with { AdminEmail = $"{localPart}@example.com" };

        Assert.Equal(256, request.AdminEmail.Length);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Password_Is_Whitespace_Only()
    {
        var validator = new SignUpValidator();
        var request = ValidRequest() with { Password = "        " };

        var result = validator.Validate(request);

        // NotEmpty() rejects whitespace-only strings in FluentValidation.
        Assert.False(result.IsValid);
    }
}

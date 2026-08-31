using HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

namespace HR.Modules.Identity.Tests;

public class ResetPlatformAdministratorMfaValidatorTests
{
    private readonly ResetPlatformAdministratorMfaValidator _validator = new();

    private static ResetPlatformAdministratorMfaRequest Valid() =>
        new(Guid.NewGuid(), true, "valid reason");

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Id = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPlatformAdministratorMfaRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Not_Confirmed()
    {
        var result = _validator.Validate(Valid() with { Confirmed = false });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPlatformAdministratorMfaRequest.Confirmed));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { Reason = "" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPlatformAdministratorMfaRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Whitespace()
    {
        var result = _validator.Validate(Valid() with { Reason = "   " });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPlatformAdministratorMfaRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Shorter_Than_Five_Chars()
    {
        var result = _validator.Validate(Valid() with { Reason = "abcd" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPlatformAdministratorMfaRequest.Reason));
    }

    [Fact]
    public void Validate_Passes_When_Reason_Is_Exactly_Five_Chars()
    {
        Assert.True(_validator.Validate(Valid() with { Reason = "abcde" }).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Reason_Is_Exactly_500_Chars()
    {
        Assert.True(_validator.Validate(Valid() with { Reason = new string('x', 500) }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_500_Chars()
    {
        var result = _validator.Validate(Valid() with { Reason = new string('x', 501) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResetPlatformAdministratorMfaRequest.Reason));
    }
}

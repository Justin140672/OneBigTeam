using HR.Modules.Companies.Features.ForceCustomerReadOnly;

namespace HR.Modules.Companies.Tests;

public class ForceCustomerReadOnlyValidatorTests
{
    private static ForceCustomerReadOnlyRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Reason = "Suspected abuse - forcing read-only pending investigation.",
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new ForceCustomerReadOnlyValidator().Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new ForceCustomerReadOnlyValidator().Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ForceCustomerReadOnlyRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = new ForceCustomerReadOnlyValidator().Validate(ValidRequest() with { Reason = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ForceCustomerReadOnlyRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Too_Short()
    {
        var result = new ForceCustomerReadOnlyValidator().Validate(ValidRequest() with { Reason = "abcd" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ForceCustomerReadOnlyRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_1000_Characters()
    {
        var result = new ForceCustomerReadOnlyValidator().Validate(ValidRequest() with { Reason = new string('A', 1001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ForceCustomerReadOnlyRequest.Reason));
    }
}

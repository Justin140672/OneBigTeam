using HR.Modules.Companies.Features.ReinstateCustomerSubscription;

namespace HR.Modules.Companies.Tests;

public class ReinstateCustomerSubscriptionValidatorTests
{
    private static ReinstateCustomerSubscriptionRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Reason = "Customer resolved billing dispute; reinstating access.",
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new ReinstateCustomerSubscriptionValidator().Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new ReinstateCustomerSubscriptionValidator().Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ReinstateCustomerSubscriptionRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = new ReinstateCustomerSubscriptionValidator().Validate(ValidRequest() with { Reason = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ReinstateCustomerSubscriptionRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Too_Short()
    {
        var result = new ReinstateCustomerSubscriptionValidator().Validate(ValidRequest() with { Reason = "abcd" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ReinstateCustomerSubscriptionRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_1000_Characters()
    {
        var result = new ReinstateCustomerSubscriptionValidator().Validate(ValidRequest() with { Reason = new string('A', 1001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ReinstateCustomerSubscriptionRequest.Reason));
    }
}

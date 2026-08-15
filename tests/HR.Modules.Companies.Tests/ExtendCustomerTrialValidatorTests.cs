using HR.Modules.Companies.Features.ExtendCustomerTrial;

namespace HR.Modules.Companies.Tests;

public class ExtendCustomerTrialValidatorTests
{
    private static ExtendCustomerTrialRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        NewTrialExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        Reason = "Customer requested more time to evaluate.",
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new ExtendCustomerTrialValidator().Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new ExtendCustomerTrialValidator().Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExtendCustomerTrialRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_NewTrialExpiresAt_Is_Default()
    {
        var result = new ExtendCustomerTrialValidator().Validate(ValidRequest() with { NewTrialExpiresAt = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExtendCustomerTrialRequest.NewTrialExpiresAt));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = new ExtendCustomerTrialValidator().Validate(ValidRequest() with { Reason = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExtendCustomerTrialRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Too_Short()
    {
        var result = new ExtendCustomerTrialValidator().Validate(ValidRequest() with { Reason = "abcd" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExtendCustomerTrialRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_1000_Characters()
    {
        var result = new ExtendCustomerTrialValidator().Validate(ValidRequest() with { Reason = new string('A', 1001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExtendCustomerTrialRequest.Reason));
    }
}

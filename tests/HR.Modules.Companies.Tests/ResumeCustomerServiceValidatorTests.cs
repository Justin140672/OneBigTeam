using HR.Modules.Companies.Features.ResumeCustomerService;

namespace HR.Modules.Companies.Tests;

public class ResumeCustomerServiceValidatorTests
{
    private static ResumeCustomerServiceRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Reason = "Investigation concluded - restoring normal service.",
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new ResumeCustomerServiceValidator().Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new ResumeCustomerServiceValidator().Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResumeCustomerServiceRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = new ResumeCustomerServiceValidator().Validate(ValidRequest() with { Reason = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResumeCustomerServiceRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Too_Short()
    {
        var result = new ResumeCustomerServiceValidator().Validate(ValidRequest() with { Reason = "abcd" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResumeCustomerServiceRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_1000_Characters()
    {
        var result = new ResumeCustomerServiceValidator().Validate(ValidRequest() with { Reason = new string('A', 1001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ResumeCustomerServiceRequest.Reason));
    }
}

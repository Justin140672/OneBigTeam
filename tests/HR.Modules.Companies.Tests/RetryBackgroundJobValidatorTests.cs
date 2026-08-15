using HR.Modules.Companies.Features.RetryBackgroundJob;

namespace HR.Modules.Companies.Tests;

public class RetryBackgroundJobValidatorTests
{
    private static RetryBackgroundJobRequest ValidRequest() => new()
    {
        JobId = "job-1",
        Reason = "Investigating a transient failure before retrying.",
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new RetryBackgroundJobValidator().Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_JobId_Is_Empty()
    {
        var result = new RetryBackgroundJobValidator().Validate(ValidRequest() with { JobId = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RetryBackgroundJobRequest.JobId));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = new RetryBackgroundJobValidator().Validate(ValidRequest() with { Reason = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RetryBackgroundJobRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Too_Short()
    {
        var result = new RetryBackgroundJobValidator().Validate(ValidRequest() with { Reason = "abcd" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RetryBackgroundJobRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_1000_Characters()
    {
        var result = new RetryBackgroundJobValidator().Validate(ValidRequest() with { Reason = new string('A', 1001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RetryBackgroundJobRequest.Reason));
    }
}

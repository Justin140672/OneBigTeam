using HR.Modules.Offboarding.Features.WaiveOffboardingTask;

namespace HR.Modules.Offboarding.Tests;

public class WaiveOffboardingTaskValidatorTests
{
    private static WaiveOffboardingTaskRequest ValidRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Not required for this departure.");

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new WaiveOffboardingTaskValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new WaiveOffboardingTaskValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WaiveOffboardingTaskRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_OffboardingTaskId_Is_Empty()
    {
        var validator = new WaiveOffboardingTaskValidator();
        var request = ValidRequest() with { OffboardingTaskId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WaiveOffboardingTaskRequest.OffboardingTaskId));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Null()
    {
        var validator = new WaiveOffboardingTaskValidator();
        var request = ValidRequest() with { Reason = null! };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WaiveOffboardingTaskRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty_String()
    {
        var validator = new WaiveOffboardingTaskValidator();
        var request = ValidRequest() with { Reason = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WaiveOffboardingTaskRequest.Reason));
    }

    // NotEmpty() treats whitespace-only strings as empty, distinct from a bare length check —
    // pins that behaviour explicitly rather than relying on the null/empty cases above alone.
    [Fact]
    public void Validate_Fails_When_Reason_Is_Whitespace_Only()
    {
        var validator = new WaiveOffboardingTaskValidator();
        var request = ValidRequest() with { Reason = "   " };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WaiveOffboardingTaskRequest.Reason));
    }

    [Fact]
    public void Validate_Passes_When_Reason_Is_Exactly_MaxLength()
    {
        var validator = new WaiveOffboardingTaskValidator();
        var request = ValidRequest() with { Reason = new string('a', 2000) };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_MaxLength_By_One()
    {
        var validator = new WaiveOffboardingTaskValidator();
        var request = ValidRequest() with { Reason = new string('a', 2001) };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WaiveOffboardingTaskRequest.Reason));
    }
}

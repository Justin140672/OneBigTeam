using HR.Modules.Offboarding.Features.StartOffboarding;

namespace HR.Modules.Offboarding.Tests;

public class StartOffboardingValidatorTests
{
    private static StartOffboardingRequest ValidRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 15), "Some notes.");

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new StartOffboardingValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartOffboardingRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { EmployeeId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartOffboardingRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LastWorkingDay_Is_Default()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { LastWorkingDay = default };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartOffboardingRequest.LastWorkingDay));
    }

    [Fact]
    public void Validate_Passes_When_LastWorkingDay_Is_Any_NonDefault_Date()
    {
        var validator = new StartOffboardingValidator();
        // Boundary: the single day immediately after default(DateOnly) must already be valid —
        // proves the rule is an exact NotEqual(default) check, not an off-by-one "must be after
        // some other date" comparison.
        var request = ValidRequest() with { LastWorkingDay = default(DateOnly).AddDays(1) };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Notes_Is_Null()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { Notes = null };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Notes_Is_Empty_String()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { Notes = string.Empty };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Notes_Is_Exactly_MaxLength()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { Notes = new string('a', 2000) };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_MaxLength_By_One()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { Notes = new string('a', 2001) };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartOffboardingRequest.Notes));
    }

    [Fact]
    public void Validate_Fails_When_ReplacementManagerEmployeeId_Equals_EmployeeId()
    {
        var validator = new StartOffboardingValidator();
        var employeeId = Guid.NewGuid();
        var request = ValidRequest() with { EmployeeId = employeeId, ReplacementManagerEmployeeId = employeeId };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartOffboardingRequest.ReplacementManagerEmployeeId));
    }

    [Fact]
    public void Validate_Passes_When_ReplacementManagerEmployeeId_Differs_From_EmployeeId()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { ReplacementManagerEmployeeId = Guid.NewGuid() };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_ReplacementManagerEmployeeId_Is_Null()
    {
        var validator = new StartOffboardingValidator();
        var request = ValidRequest() with { ReplacementManagerEmployeeId = null };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

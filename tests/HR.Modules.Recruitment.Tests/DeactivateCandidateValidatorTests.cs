using HR.Modules.Recruitment.Features.DeactivateCandidate;

namespace HR.Modules.Recruitment.Tests;

public class DeactivateCandidateValidatorTests
{
    private readonly DeactivateCandidateValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid(), "No longer available"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.Empty, Guid.NewGuid(), "No longer available"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateCandidateRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_CandidateId_Is_Empty()
    {
        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.NewGuid(), Guid.Empty, "No longer available"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateCandidateRequest.CandidateId));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Null()
    {
        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid(), null!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateCandidateRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid(), string.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateCandidateRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Whitespace_Only()
    {
        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid(), "   "));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateCandidateRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_With_Correct_Message_When_Reason_Is_Empty()
    {
        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid(), string.Empty));

        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(DeactivateCandidateRequest.Reason) &&
            e.ErrorMessage == "A reason is required to deactivate a candidate.");
    }

    [Fact]
    public void Validate_Passes_When_Reason_Is_Exactly_MaxLength()
    {
        var reason = new string('a', 1000);

        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid(), reason));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_MaxLength()
    {
        var reason = new string('a', 1001);

        var result = _validator.Validate(new DeactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid(), reason));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeactivateCandidateRequest.Reason));
    }
}

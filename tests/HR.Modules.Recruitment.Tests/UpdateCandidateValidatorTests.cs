using HR.Modules.Recruitment.Features.UpdateCandidate;

namespace HR.Modules.Recruitment.Tests;

public class UpdateCandidateValidatorTests
{
    private readonly UpdateCandidateValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new UpdateCandidateRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            FirstName   = "Emma",
            LastName    = "Clarke",
            Email       = "emma.clarke@example.com",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CandidateId_Is_Empty()
    {
        var result = _validator.Validate(new UpdateCandidateRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.Empty,
            FirstName   = "Emma",
            LastName    = "Clarke",
            Email       = "emma.clarke@example.com",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCandidateRequest.CandidateId));
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Invalid()
    {
        var result = _validator.Validate(new UpdateCandidateRequest
        {
            CompanyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            FirstName   = "Emma",
            LastName    = "Clarke",
            Email       = "not-an-email",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCandidateRequest.Email));
    }
}

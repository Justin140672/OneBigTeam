using HR.Modules.Recruitment.Features.RejectCandidate;

namespace HR.Modules.Recruitment.Tests;

public class RejectCandidateValidatorTests
{
    private readonly RejectCandidateValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new RejectCandidateRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_With_RejectionReason()
    {
        var result = _validator.Validate(new RejectCandidateRequest
        {
            CompanyId       = Guid.NewGuid(),
            VacancyId       = Guid.NewGuid(),
            ApplicationId   = Guid.NewGuid(),
            RejectionReason = "Not enough experience.",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Is_Empty()
    {
        var result = _validator.Validate(new RejectCandidateRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectCandidateRequest.ApplicationId));
    }

    [Fact]
    public void Validate_Fails_When_RejectionReason_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new RejectCandidateRequest
        {
            CompanyId       = Guid.NewGuid(),
            VacancyId       = Guid.NewGuid(),
            ApplicationId   = Guid.NewGuid(),
            RejectionReason = new string('A', 2001),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectCandidateRequest.RejectionReason));
    }
}

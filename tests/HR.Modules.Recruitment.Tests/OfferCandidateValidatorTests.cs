using HR.Modules.Recruitment.Features.OfferCandidate;

namespace HR.Modules.Recruitment.Tests;

public class OfferCandidateValidatorTests
{
    private readonly OfferCandidateValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new OfferCandidateRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Is_Empty()
    {
        var result = _validator.Validate(new OfferCandidateRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(OfferCandidateRequest.ApplicationId));
    }
}

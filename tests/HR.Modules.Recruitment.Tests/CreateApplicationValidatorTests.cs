using HR.Modules.Recruitment.Features.CreateApplication;

namespace HR.Modules.Recruitment.Tests;

public class CreateApplicationValidatorTests
{
    private readonly CreateApplicationValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.Empty,
            CandidateId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateApplicationRequest.VacancyId));
    }

    [Fact]
    public void Validate_Fails_When_CandidateId_Is_Empty()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.NewGuid(),
            CandidateId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateApplicationRequest.CandidateId));
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateApplicationRequest
        {
            CompanyId   = Guid.NewGuid(),
            VacancyId   = Guid.NewGuid(),
            CandidateId = Guid.NewGuid(),
            Notes       = new string('A', 2001),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateApplicationRequest.Notes));
    }
}

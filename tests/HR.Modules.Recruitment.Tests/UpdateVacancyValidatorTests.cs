using HR.Modules.Recruitment.Features.UpdateVacancy;

namespace HR.Modules.Recruitment.Tests;

public class UpdateVacancyValidatorTests
{
    private readonly UpdateVacancyValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new UpdateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            VacancyId       = Guid.NewGuid(),
            Title           = "Senior Software Engineer",
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(new UpdateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            VacancyId       = Guid.Empty,
            Title           = "Senior Software Engineer",
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.VacancyId));
    }

    [Fact]
    public void Validate_Fails_When_Title_Is_Empty()
    {
        var result = _validator.Validate(new UpdateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            VacancyId       = Guid.NewGuid(),
            Title           = string.Empty,
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.Title));
    }
}

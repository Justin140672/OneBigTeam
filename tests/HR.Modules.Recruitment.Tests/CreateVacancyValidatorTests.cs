using HR.Modules.Recruitment.Features.CreateVacancy;

namespace HR.Modules.Recruitment.Tests;

public class CreateVacancyValidatorTests
{
    private readonly CreateVacancyValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            Title           = "Senior Software Engineer",
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.Empty,
            Title           = "Senior Software Engineer",
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Title_Is_Empty()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            Title           = string.Empty,
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_Title_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            Title           = new string('A', 201),
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.Title));
    }

    [Fact]
    public void Validate_Fails_When_HiringManagerId_Is_Empty()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            Title           = "Senior Software Engineer",
            HiringManagerId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.HiringManagerId));
    }

    [Fact]
    public void Validate_Fails_When_Description_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            Title           = "Senior Software Engineer",
            Description     = new string('A', 4001),
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.Description));
    }
}

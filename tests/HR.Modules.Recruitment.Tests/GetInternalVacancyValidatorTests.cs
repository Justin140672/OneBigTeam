using HR.Modules.Recruitment.Features.GetInternalVacancy;

namespace HR.Modules.Recruitment.Tests;

public class GetInternalVacancyValidatorTests
{
    private readonly GetInternalVacancyValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new GetInternalVacancyRequest
        {
            CompanyId = Guid.NewGuid(),
            VacancyId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetInternalVacancyRequest
        {
            CompanyId = Guid.Empty,
            VacancyId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetInternalVacancyRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(new GetInternalVacancyRequest
        {
            CompanyId = Guid.NewGuid(),
            VacancyId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetInternalVacancyRequest.VacancyId));
    }
}

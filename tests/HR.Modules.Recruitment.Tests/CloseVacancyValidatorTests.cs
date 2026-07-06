using HR.Modules.Recruitment.Features.CloseVacancy;

namespace HR.Modules.Recruitment.Tests;

public class CloseVacancyValidatorTests
{
    private readonly CloseVacancyValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new CloseVacancyRequest
        {
            CompanyId = Guid.NewGuid(),
            VacancyId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(new CloseVacancyRequest
        {
            CompanyId = Guid.NewGuid(),
            VacancyId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CloseVacancyRequest.VacancyId));
    }
}

using HR.Modules.Recruitment.Features.ListVacancies;

namespace HR.Modules.Recruitment.Tests;

public class ListVacanciesValidatorTests
{
    private readonly ListVacanciesValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new ListVacanciesRequest { CompanyId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new ListVacanciesRequest { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListVacanciesRequest.CompanyId));
    }
}

using HR.Modules.Recruitment.Features.ListInternalVacancies;

namespace HR.Modules.Recruitment.Tests;

public class ListInternalVacanciesValidatorTests
{
    private readonly ListInternalVacanciesValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new ListInternalVacanciesRequest { CompanyId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_With_Search_Term()
    {
        var result = _validator.Validate(new ListInternalVacanciesRequest { CompanyId = Guid.NewGuid(), Search = "engineer" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new ListInternalVacanciesRequest { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListInternalVacanciesRequest.CompanyId));
    }
}

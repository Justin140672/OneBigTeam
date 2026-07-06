using HR.Modules.Recruitment.Features.ListCandidates;

namespace HR.Modules.Recruitment.Tests;

public class ListCandidatesValidatorTests
{
    private readonly ListCandidatesValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new ListCandidatesRequest { CompanyId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new ListCandidatesRequest { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListCandidatesRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_PageNumber_Is_Less_Than_One()
    {
        var result = _validator.Validate(new ListCandidatesRequest { CompanyId = Guid.NewGuid(), PageNumber = 0 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListCandidatesRequest.PageNumber));
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Exceeds_Max()
    {
        var result = _validator.Validate(new ListCandidatesRequest { CompanyId = Guid.NewGuid(), PageSize = 101 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListCandidatesRequest.PageSize));
    }
}

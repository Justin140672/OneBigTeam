using HR.Modules.Recruitment.Features.SearchApplications;

namespace HR.Modules.Recruitment.Tests;

public class SearchApplicationsValidatorTests
{
    private readonly SearchApplicationsValidator _validator = new();

    private static SearchApplicationsRequest Valid() => new() { CompanyId = Guid.NewGuid() };

    [Fact]
    public void Validate_Passes_For_Minimal_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchApplicationsRequest.CompanyId));
    }

    // ----- Internal recruitment Ticket 6: CandidateId -----

    [Fact]
    public void Validate_Passes_When_CandidateId_Is_Null()
    {
        Assert.True(_validator.Validate(Valid() with { CandidateId = null }).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_CandidateId_Is_A_NonEmpty_Guid()
    {
        Assert.True(_validator.Validate(Valid() with { CandidateId = Guid.NewGuid() }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CandidateId_Is_Supplied_As_Guid_Empty()
    {
        var result = _validator.Validate(Valid() with { CandidateId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchApplicationsRequest.CandidateId));
    }

    // ----- Internal recruitment Ticket 6: IsInternal has no constraints (null/true/false all valid) -----

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_Passes_For_Any_IsInternal_Value(bool? isInternal)
    {
        Assert.True(_validator.Validate(Valid() with { IsInternal = isInternal }).IsValid);
    }


    [Fact]
    public void Validate_Passes_When_Search_Is_Exactly_200_Characters()
    {
        Assert.True(_validator.Validate(Valid() with { Search = new string('a', 200) }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Search_Is_201_Characters()
    {
        var result = _validator.Validate(Valid() with { Search = new string('a', 201) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchApplicationsRequest.Search));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Validate_PageNumber_Minimum_Is_Inclusive_One(int pageNumber, bool expectedValid)
    {
        Assert.Equal(expectedValid, _validator.Validate(Valid() with { PageNumber = pageNumber }).IsValid);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(200, true)]
    [InlineData(0, false)]
    [InlineData(201, false)]
    public void Validate_PageSize_Is_Inclusive_Between_1_And_200(int pageSize, bool expectedValid)
    {
        Assert.Equal(expectedValid, _validator.Validate(Valid() with { PageSize = pageSize }).IsValid);
    }
}

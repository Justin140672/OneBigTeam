using HR.Modules.Identity.Features.SearchUserAccess;

namespace HR.Modules.Identity.Tests;

public class SearchUserAccessValidatorTests
{
    private static SearchUserAccessRequest ValidRequest() => new() { CompanyId = Guid.NewGuid() };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new SearchUserAccessValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchUserAccessRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Page_Is_Zero()
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { Page = 0 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchUserAccessRequest.Page));
    }

    [Fact]
    public void Validate_Passes_When_Page_Is_One()
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { Page = 1 };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Is_Zero()
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { PageSize = 0 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchUserAccessRequest.PageSize));
    }

    [Fact]
    public void Validate_Passes_When_PageSize_Is_One()
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { PageSize = 1 };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_PageSize_Is_The_Maximum_Boundary_Of_100()
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { PageSize = 100 };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Exceeds_The_Maximum_Boundary_Of_100()
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { PageSize = 101 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchUserAccessRequest.PageSize));
    }

    [Fact]
    public void Validate_Fails_When_OverrideState_Is_Not_A_Defined_Enum_Value()
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { OverrideState = (OverrideStateFilter)999 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchUserAccessRequest.OverrideState));
    }

    [Theory]
    [InlineData(OverrideStateFilter.Any)]
    [InlineData(OverrideStateFilter.HasGrantOverride)]
    [InlineData(OverrideStateFilter.HasDenyOverride)]
    [InlineData(OverrideStateFilter.HasAnyOverride)]
    [InlineData(OverrideStateFilter.HasExpiringOverride)]
    internal void Validate_Passes_For_Every_Defined_OverrideState_Value(OverrideStateFilter overrideState)
    {
        var validator = new SearchUserAccessValidator();
        var request = ValidRequest() with { OverrideState = overrideState };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

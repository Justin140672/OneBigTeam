using HR.Modules.Identity.Features.ListUsers;

namespace HR.Modules.Identity.Tests;

public class ListUsersValidatorTests
{
    private static ListUsersRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Page = 1,
        PageSize = 25,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new ListUsersValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new ListUsersValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListUsersRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Page_Is_Less_Than_One()
    {
        var validator = new ListUsersValidator();
        var request = ValidRequest() with { Page = 0 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListUsersRequest.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Validate_Fails_When_PageSize_Out_Of_Range(int pageSize)
    {
        var validator = new ListUsersValidator();
        var request = ValidRequest() with { PageSize = pageSize };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListUsersRequest.PageSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Validate_Passes_When_PageSize_Is_At_Inclusive_Bounds(int pageSize)
    {
        var validator = new ListUsersValidator();
        var request = ValidRequest() with { PageSize = pageSize };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Page_Is_Exactly_One()
    {
        var validator = new ListUsersValidator();
        var request = ValidRequest() with { Page = 1 };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}

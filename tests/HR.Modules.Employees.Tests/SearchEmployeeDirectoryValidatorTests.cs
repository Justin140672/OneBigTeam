using HR.Modules.Employees.Features.SearchEmployeeDirectory;

namespace HR.Modules.Employees.Tests;

public class SearchEmployeeDirectoryValidatorTests
{
    private static readonly SearchEmployeeDirectoryValidator Validator = new();

    private static SearchEmployeeDirectoryRequest Valid(
        string? term = null, int limit = 20) =>
        new(Guid.NewGuid(), term, false, limit);

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
    {
        var result = Validator.Validate(new SearchEmployeeDirectoryRequest(Guid.Empty, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDirectoryRequest.CompanyId));
    }

    [Fact]
    public void Passes_When_CompanyId_Is_Populated()
    {
        Assert.True(Validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Null_Term_Is_Allowed()
    {
        Assert.True(Validator.Validate(Valid(term: null)).IsValid);
    }

    [Fact]
    public void Term_Of_200_Chars_Passes()
    {
        Assert.True(Validator.Validate(Valid(term: new string('a', 200))).IsValid);
    }

    [Fact]
    public void Term_Of_201_Chars_Fails()
    {
        var result = Validator.Validate(Valid(term: new string('a', 201)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDirectoryRequest.Term));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Limit_Outside_1_To_50_Fails(int limit)
    {
        var result = Validator.Validate(Valid(limit: limit));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchEmployeeDirectoryRequest.Limit));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void Limit_On_Boundary_Passes(int limit)
    {
        Assert.True(Validator.Validate(Valid(limit: limit)).IsValid);
    }
}

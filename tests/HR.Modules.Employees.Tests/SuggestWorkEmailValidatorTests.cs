using HR.Modules.Employees.Features.SuggestWorkEmail;

namespace HR.Modules.Employees.Tests;

public class SuggestWorkEmailValidatorTests
{
    private readonly SuggestWorkEmailValidator _validator = new();

    private static SuggestWorkEmailRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        FirstName = "Jane",
        LastName = "Smith",
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Names_Are_Null_Or_Whitespace()
    {
        Assert.True(_validator.Validate(ValidRequest() with { FirstName = null, LastName = null }).IsValid);
        Assert.True(_validator.Validate(ValidRequest() with { FirstName = "  ", LastName = "  " }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SuggestWorkEmailRequest.CompanyId));
    }

    [Fact]
    public void Validate_Passes_When_FirstName_Is_At_Maximum_Length()
    {
        Assert.True(_validator.Validate(ValidRequest() with { FirstName = new string('a', 100) }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Exceeds_Maximum_Length()
    {
        var result = _validator.Validate(ValidRequest() with { FirstName = new string('a', 101) });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SuggestWorkEmailRequest.FirstName));
    }

    [Fact]
    public void Validate_Passes_When_LastName_Is_At_Maximum_Length()
    {
        Assert.True(_validator.Validate(ValidRequest() with { LastName = new string('a', 100) }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LastName_Exceeds_Maximum_Length()
    {
        var result = _validator.Validate(ValidRequest() with { LastName = new string('a', 101) });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SuggestWorkEmailRequest.LastName));
    }
}

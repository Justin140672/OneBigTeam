using HR.Modules.Recruitment.Features.CreateCandidate;

namespace HR.Modules.Recruitment.Tests;

public class CreateCandidateValidatorTests
{
    private readonly CreateCandidateValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new CreateCandidateRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Emma",
            LastName  = "Clarke",
            Email     = "emma.clarke@example.com",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new CreateCandidateRequest
        {
            CompanyId = Guid.Empty,
            FirstName = "Emma",
            LastName  = "Clarke",
            Email     = "emma.clarke@example.com",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Is_Empty()
    {
        var result = _validator.Validate(new CreateCandidateRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = string.Empty,
            LastName  = "Clarke",
            Email     = "emma.clarke@example.com",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateRequest.FirstName));
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Invalid()
    {
        var result = _validator.Validate(new CreateCandidateRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Emma",
            LastName  = "Clarke",
            Email     = "not-an-email",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateRequest.Email));
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Empty()
    {
        var result = _validator.Validate(new CreateCandidateRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Emma",
            LastName  = "Clarke",
            Email     = string.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateRequest.Email));
    }

    [Fact]
    public void Validate_Fails_When_Phone_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateCandidateRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Emma",
            LastName  = "Clarke",
            Email     = "emma.clarke@example.com",
            Phone     = new string('1', 31),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCandidateRequest.Phone));
    }
}

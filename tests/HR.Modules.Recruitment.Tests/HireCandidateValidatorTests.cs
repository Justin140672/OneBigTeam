using HR.Modules.Recruitment.Features.HireCandidate;

namespace HR.Modules.Recruitment.Tests;

public class HireCandidateValidatorTests
{
    private readonly HireCandidateValidator _validator = new();

    private static HireCandidateRequest ValidRequest() => new()
    {
        CompanyId         = Guid.NewGuid(),
        VacancyId         = Guid.NewGuid(),
        ApplicationId     = Guid.NewGuid(),
        StartDate         = new DateOnly(2026, 8, 1),
        DateOfBirth       = new DateOnly(1995, 3, 20),
        Nationality       = "British",
        Gender            = "Female",
        EmployeeNumber    = "EMP-0001",
        EmploymentTypeId  = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_EmployeeNumber_Is_Blank()
    {
        // Deliberately not enforced by NotEmpty here: in Automatic employee-numbering mode the
        // Hire Candidate dialog no longer shows the field at all (see item 31), and
        // HireCandidateHandler forwards a blank value through to CreateEmployeeHandler, which
        // generates one and separately enforces requiredness for Manual-mode companies.
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = string.Empty });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { ApplicationId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HireCandidateRequest.ApplicationId));
    }

    [Fact]
    public void Validate_Passes_When_StartDate_Is_Null()
    {
        // Ticket 2: StartDate is now optional — the handler falls back to the accepted offer's
        // proposed start date, and fails the hire itself if neither is present.
        var result = _validator.Validate(ValidRequest() with { StartDate = null });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_StartDate_Is_Supplied_But_Default_Value()
    {
        var result = _validator.Validate(ValidRequest() with { StartDate = new DateOnly() });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HireCandidateRequest.StartDate));
    }

    [Fact]
    public void Validate_Fails_When_Nationality_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { Nationality = string.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HireCandidateRequest.Nationality));
    }

    [Fact]
    public void Validate_Fails_When_Gender_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { Gender = string.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HireCandidateRequest.Gender));
    }
}

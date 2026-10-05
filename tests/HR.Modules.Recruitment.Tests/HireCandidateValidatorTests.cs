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
        AddressLine1      = "1 High Street",
        City              = "London",
        PostCode          = "SW1A 1AA",
    };

    [Theory]
    [InlineData(nameof(HireCandidateRequest.AddressLine1))]
    [InlineData(nameof(HireCandidateRequest.City))]
    [InlineData(nameof(HireCandidateRequest.PostCode))]
    public void Validate_Fails_When_Required_Address_Field_Is_Null_Empty_Or_Whitespace(string property)
    {
        foreach (var blank in new string?[] { null, string.Empty, "   " })
        {
            var request = property switch
            {
                nameof(HireCandidateRequest.AddressLine1) => ValidRequest() with { AddressLine1 = blank },
                nameof(HireCandidateRequest.City) => ValidRequest() with { City = blank },
                _ => ValidRequest() with { PostCode = blank },
            };

            var result = _validator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == property);
        }
    }

    [Fact]
    public void Validate_Passes_When_Optional_Address_Fields_Are_Blank()
    {
        var result = _validator.Validate(ValidRequest() with { AddressLine2 = null, County = "  " });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Address_Fields_Exceed_Max_Length()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            AddressLine1 = new string('a', 201),
            City = new string('b', 101),
            PostCode = new string('c', 21),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HireCandidateRequest.AddressLine1));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HireCandidateRequest.City));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(HireCandidateRequest.PostCode));
    }

    [Fact]
    public void Validate_Passes_With_No_Manager_Override_And_Null_ManagerId()
    {
        var result = _validator.Validate(ValidRequest() with { OverrideManager = true, ManagerId = null });

        Assert.True(result.IsValid);
    }

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

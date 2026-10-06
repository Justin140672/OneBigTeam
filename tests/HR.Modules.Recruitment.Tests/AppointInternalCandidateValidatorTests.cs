using HR.Modules.Recruitment.Features.AppointInternalCandidate;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 7: request validation for completing an internal application by
/// internal appointment. Exactly one of ManagerId / NoManager must be chosen, and the compensation
/// fields are only required (and only validated) when CreateCompensationChange is set.
/// </summary>
public class AppointInternalCandidateValidatorTests
{
    private readonly AppointInternalCandidateValidator _validator = new();

    private static AppointInternalCandidateRequest ValidRequest() => new()
    {
        CompanyId     = Guid.NewGuid(),
        VacancyId     = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        EffectiveDate = new DateOnly(2026, 10, 1),
        ManagerId     = Guid.NewGuid(),
    };

    private static AppointInternalCandidateRequest ValidWithCompensation() => ValidRequest() with
    {
        CreateCompensationChange = true,
        CompensationSalaryType   = "Annual",
        CompensationSalary       = 65000m,
        CompensationCurrency     = "GBP",
        CompensationHoursPerWeek = 37.5m,
        CompensationFte          = 1m,
        CompensationNotes        = "Salary for the new role.",
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request_With_Compensation()
    {
        Assert.True(_validator.Validate(ValidWithCompensation()).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_EffectiveDate_Omitted()
    {
        Assert.True(_validator.Validate(ValidRequest() with { EffectiveDate = null }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EffectiveDate_Is_Default()
    {
        var result = _validator.Validate(ValidRequest() with { EffectiveDate = default(DateOnly) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.EffectiveDate));
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { VacancyId = Guid.Empty });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.VacancyId));
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { ApplicationId = Guid.Empty });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.ApplicationId));
    }


    [Fact]
    public void Validate_Passes_When_Neither_Manager_Nor_NoManager_Chosen_Because_Accepted_Offer_Supplies_It()
    {
        var result = _validator.Validate(ValidRequest() with { ManagerId = null, NoManager = false });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ManagerId_Is_Empty_Guid_Without_NoManager()
    {
        var result = _validator.Validate(ValidRequest() with { ManagerId = Guid.Empty, NoManager = false });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.ManagerId));
    }

    [Fact]
    public void Validate_Passes_When_NoManager_Chosen_Without_ManagerId()
    {
        Assert.True(_validator.Validate(ValidRequest() with { ManagerId = null, NoManager = true }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Both_ManagerId_And_NoManager_Chosen()
    {
        var result = _validator.Validate(ValidRequest() with { ManagerId = Guid.NewGuid(), NoManager = true });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.ManagerId));
    }


    [Fact]
    public void Validate_Ignores_Invalid_Compensation_Fields_When_No_Compensation_Change()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            CreateCompensationChange = false,
            CompensationSalaryType   = "Weekly",
            CompensationSalary       = -1m,
            CompensationCurrency     = "POUNDS",
            CompensationHoursPerWeek = 0m,
            CompensationFte          = 2m,
            CompensationNotes        = new string('x', 4001),
        });

        Assert.True(result.IsValid);
    }


    [Theory]
    [InlineData("Annual")]
    [InlineData("Hourly")]
    [InlineData("Daily")]
    [InlineData("annual")]
    [InlineData("HOURLY")]
    public void Validate_Passes_For_Supported_SalaryType_Case_Insensitively(string salaryType)
    {
        Assert.True(_validator.Validate(ValidWithCompensation() with { CompensationSalaryType = salaryType }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Weekly")]
    [InlineData("Monthly")]
    public void Validate_Fails_For_Missing_Or_Unsupported_SalaryType(string? salaryType)
    {
        var result = _validator.Validate(ValidWithCompensation() with { CompensationSalaryType = salaryType });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.CompensationSalaryType));
    }


    [Fact]
    public void Validate_Fails_When_Salary_Missing()
    {
        var result = _validator.Validate(ValidWithCompensation() with { CompensationSalary = null });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.CompensationSalary));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    [InlineData("-50000")]
    public void Validate_Fails_When_Salary_Not_Positive(string salary)
    {
        var result = _validator.Validate(ValidWithCompensation() with { CompensationSalary = decimal.Parse(salary) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.CompensationSalary));
    }

    [Fact]
    public void Validate_Passes_When_Salary_Is_Smallest_Positive_Value()
    {
        Assert.True(_validator.Validate(ValidWithCompensation() with { CompensationSalary = 0.01m }).IsValid);
    }


    [Fact]
    public void Validate_Passes_For_Three_Letter_Currency()
    {
        Assert.True(_validator.Validate(ValidWithCompensation() with { CompensationCurrency = "EUR" }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GB")]
    [InlineData("GBPX")]
    public void Validate_Fails_For_Missing_Or_Wrong_Length_Currency(string? currency)
    {
        var result = _validator.Validate(ValidWithCompensation() with { CompensationCurrency = currency });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.CompensationCurrency));
    }


    [Fact]
    public void Validate_Passes_When_HoursPerWeek_Omitted()
    {
        Assert.True(_validator.Validate(ValidWithCompensation() with { CompensationHoursPerWeek = null }).IsValid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Validate_Fails_When_HoursPerWeek_Not_Positive(string hours)
    {
        var result = _validator.Validate(ValidWithCompensation() with { CompensationHoursPerWeek = decimal.Parse(hours) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.CompensationHoursPerWeek));
    }

    [Fact]
    public void Validate_Passes_When_HoursPerWeek_Is_Just_Above_Zero()
    {
        Assert.True(_validator.Validate(ValidWithCompensation() with { CompensationHoursPerWeek = 0.01m }).IsValid);
    }


    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("1")]
    public void Validate_Passes_For_Fte_Within_Inclusive_Bounds(string fte)
    {
        Assert.True(_validator.Validate(ValidWithCompensation() with { CompensationFte = decimal.Parse(fte) }).IsValid);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    public void Validate_Fails_For_Fte_Outside_Bounds(string fte)
    {
        var result = _validator.Validate(ValidWithCompensation() with { CompensationFte = decimal.Parse(fte) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.CompensationFte));
    }

    [Fact]
    public void Validate_Passes_When_Fte_Omitted()
    {
        Assert.True(_validator.Validate(ValidWithCompensation() with { CompensationFte = null }).IsValid);
    }


    [Fact]
    public void Validate_Passes_When_Notes_At_Max_Length()
    {
        Assert.True(_validator.Validate(ValidWithCompensation() with { CompensationNotes = new string('x', 4000) }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceed_Max_Length()
    {
        var result = _validator.Validate(ValidWithCompensation() with { CompensationNotes = new string('x', 4001) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AppointInternalCandidateRequest.CompensationNotes));
    }
}

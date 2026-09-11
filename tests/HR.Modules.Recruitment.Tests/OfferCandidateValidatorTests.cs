using HR.Modules.Recruitment.Features.OfferCandidate;

namespace HR.Modules.Recruitment.Tests;

public class OfferCandidateValidatorTests
{
    private readonly OfferCandidateValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new OfferCandidateRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Is_Empty()
    {
        var result = _validator.Validate(new OfferCandidateRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(OfferCandidateRequest.ApplicationId));
    }

    // Ticket 2: optional offer-term fields.

    private static OfferCandidateRequest Valid() => new()
    {
        CompanyId     = Guid.NewGuid(),
        VacancyId     = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_Passes_When_All_Optional_Offer_Fields_Are_Omitted()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50000)]
    public void Validate_Fails_When_OfferedSalary_Is_Not_Greater_Than_Zero(decimal salary)
    {
        var result = _validator.Validate(Valid() with { OfferedSalary = salary });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(OfferCandidateRequest.OfferedSalary));
    }

    [Fact]
    public void Validate_Passes_When_OfferedSalary_Is_Positive()
    {
        Assert.True((_validator.Validate(Valid() with { OfferedSalary = 0.01m })).IsValid);
    }

    [Theory]
    [InlineData("Annual")]
    [InlineData("annual")]
    [InlineData("HOURLY")]
    [InlineData("Daily")]
    public void Validate_Passes_For_Valid_Frequency_Case_Insensitive(string frequency)
    {
        Assert.True(_validator.Validate(Valid() with { OfferedSalaryFrequency = frequency }).IsValid);
    }

    [Theory]
    [InlineData("Weekly")]
    [InlineData("per annum")]
    [InlineData("123")]
    public void Validate_Fails_For_Unrecognised_Frequency(string frequency)
    {
        var result = _validator.Validate(Valid() with { OfferedSalaryFrequency = frequency });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(OfferCandidateRequest.OfferedSalaryFrequency));
    }

    [Fact]
    public void Validate_Skips_Frequency_Rule_When_Frequency_Is_Whitespace_Or_Null()
    {
        Assert.True(_validator.Validate(Valid() with { OfferedSalaryFrequency = null }).IsValid);
        Assert.True(_validator.Validate(Valid() with { OfferedSalaryFrequency = "   " }).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_OfferNotes_Is_Exactly_2000_Chars()
    {
        Assert.True(_validator.Validate(Valid() with { OfferNotes = new string('x', 2000) }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_OfferNotes_Exceeds_2000_Chars()
    {
        var result = _validator.Validate(Valid() with { OfferNotes = new string('x', 2001) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(OfferCandidateRequest.OfferNotes));
    }
}

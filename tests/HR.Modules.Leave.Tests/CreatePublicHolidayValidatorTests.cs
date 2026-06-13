using HR.Modules.Leave.Features.CreatePublicHoliday;

namespace HR.Modules.Leave.Tests;

public class CreatePublicHolidayValidatorTests
{
    private static CreatePublicHolidayRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Date = new DateOnly(2026, 12, 25),
        Name = "Christmas Day",
        CountryCode = "GB"
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new CreatePublicHolidayValidator().Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new CreatePublicHolidayValidator().Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePublicHolidayRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Date_Is_Default()
    {
        var result = new CreatePublicHolidayValidator().Validate(ValidRequest() with { Date = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePublicHolidayRequest.Date));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var result = new CreatePublicHolidayValidator().Validate(ValidRequest() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePublicHolidayRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_200_Characters()
    {
        var result = new CreatePublicHolidayValidator().Validate(ValidRequest() with { Name = new string('A', 201) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePublicHolidayRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Is_Empty()
    {
        var result = new CreatePublicHolidayValidator().Validate(ValidRequest() with { CountryCode = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePublicHolidayRequest.CountryCode));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Exceeds_10_Characters()
    {
        var result = new CreatePublicHolidayValidator().Validate(ValidRequest() with { CountryCode = new string('X', 11) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreatePublicHolidayRequest.CountryCode));
    }
}

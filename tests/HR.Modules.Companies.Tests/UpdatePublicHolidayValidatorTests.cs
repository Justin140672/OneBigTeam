using HR.Modules.Companies.Features.UpdatePublicHoliday;

namespace HR.Modules.Companies.Tests;

public class UpdatePublicHolidayValidatorTests
{
    private static UpdatePublicHolidayRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        Date = new DateOnly(2026, 12, 25),
        Name = "Christmas Day",
        CountryCode = "GB"
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new UpdatePublicHolidayValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new UpdatePublicHolidayValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePublicHolidayRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var v = new UpdatePublicHolidayValidator();
        var result = v.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePublicHolidayRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Date_Is_Default()
    {
        var v = new UpdatePublicHolidayValidator();
        var result = v.Validate(ValidRequest() with { Date = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePublicHolidayRequest.Date));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var v = new UpdatePublicHolidayValidator();
        var result = v.Validate(ValidRequest() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePublicHolidayRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_200_Characters()
    {
        var v = new UpdatePublicHolidayValidator();
        var result = v.Validate(ValidRequest() with { Name = new string('N', 201) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePublicHolidayRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Is_Empty()
    {
        var v = new UpdatePublicHolidayValidator();
        var result = v.Validate(ValidRequest() with { CountryCode = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePublicHolidayRequest.CountryCode));
    }

    [Fact]
    public void Validate_Fails_When_CountryCode_Exceeds_10_Characters()
    {
        var v = new UpdatePublicHolidayValidator();
        var result = v.Validate(ValidRequest() with { CountryCode = new string('X', 11) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePublicHolidayRequest.CountryCode));
    }

    [Fact]
    public void Validate_Passes_When_Name_Is_At_Max_Length()
    {
        var v = new UpdatePublicHolidayValidator();
        var result = v.Validate(ValidRequest() with { Name = new string('N', 200) });
        Assert.True(result.IsValid);
    }
}

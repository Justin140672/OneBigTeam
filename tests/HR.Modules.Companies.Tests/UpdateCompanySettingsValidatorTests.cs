using HR.Modules.Companies.Features.UpdateCompanySettings;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanySettingsValidatorTests
{
	private static UpdateCompanySettingsRequest ValidRequest() => new()
	{
		Id = Guid.NewGuid(),
		TimeZone = "Europe/London",
		Locale = "en-GB",
	};

	[Fact]
	public void Validate_Passes_For_Valid_Request()
	{
		var validator = new UpdateCompanySettingsValidator();
		Assert.True(validator.Validate(ValidRequest()).IsValid);
	}

	[Fact]
	public void Validate_Fails_When_Id_Is_Empty()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { Id = Guid.Empty });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanySettingsRequest.Id));
	}

	[Fact]
	public void Validate_Fails_When_TimeZone_Is_Empty()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { TimeZone = string.Empty });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanySettingsRequest.TimeZone));
	}

	[Fact]
	public void Validate_Fails_When_TimeZone_Exceeds_MaximumLength()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { TimeZone = new string('a', 101) });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanySettingsRequest.TimeZone));
	}

	[Fact]
	public void Validate_Fails_When_Locale_Is_Empty()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { Locale = string.Empty });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanySettingsRequest.Locale));
	}

	[Fact]
	public void Validate_Fails_When_Locale_Exceeds_MaximumLength()
	{
		var validator = new UpdateCompanySettingsValidator();
		var result = validator.Validate(ValidRequest() with { Locale = new string('a', 21) });
		Assert.False(result.IsValid);
		Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompanySettingsRequest.Locale));
	}
}

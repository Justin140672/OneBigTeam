using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Features.UpdateWorkEmailSettings;

namespace HR.Modules.Companies.Tests;

public class UpdateWorkEmailSettingsValidatorTests
{
    private readonly UpdateWorkEmailSettingsValidator _validator = new();

    private static UpdateWorkEmailSettingsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        SuggestionsEnabled = true,
        PrimaryDomain = "example.com",
        NamingConvention = WorkEmailNamingConvention.FirstNameDotLastName,
        Version = 1,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateWorkEmailSettingsRequest.CompanyId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Fails_When_Version_Is_Not_Positive(int version)
    {
        var result = _validator.Validate(ValidRequest() with { Version = version });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateWorkEmailSettingsRequest.Version));
    }

    [Fact]
    public void Validate_Passes_When_Version_Is_One()
    {
        Assert.True(_validator.Validate(ValidRequest() with { Version = 1 }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_NamingConvention_Is_Not_Defined()
    {
        var result = _validator.Validate(ValidRequest() with { NamingConvention = (WorkEmailNamingConvention)0 });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateWorkEmailSettingsRequest.NamingConvention));
    }

    [Theory]
    [InlineData(WorkEmailNamingConvention.FirstNameDotLastName)]
    [InlineData(WorkEmailNamingConvention.FirstInitialDotLastName)]
    [InlineData(WorkEmailNamingConvention.FirstNameLastName)]
    [InlineData(WorkEmailNamingConvention.FirstName)]
    public void Validate_Passes_For_Each_Defined_NamingConvention(WorkEmailNamingConvention convention)
    {
        Assert.True(_validator.Validate(ValidRequest() with { NamingConvention = convention }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_When_PrimaryDomain_Is_Missing_Whether_Enabled_Or_Not(string? primary)
    {
        var enabled = _validator.Validate(ValidRequest() with { PrimaryDomain = primary });
        var disabled = _validator.Validate(ValidRequest() with { SuggestionsEnabled = false, PrimaryDomain = primary });

        Assert.Contains(enabled.Errors, e => e.PropertyName == nameof(UpdateWorkEmailSettingsRequest.PrimaryDomain));
        Assert.Contains(disabled.Errors, e => e.PropertyName == nameof(UpdateWorkEmailSettingsRequest.PrimaryDomain));
    }

    [Fact]
    public void Validate_Passes_When_Disabled_With_A_PrimaryDomain()
    {
        Assert.True(_validator.Validate(ValidRequest() with { SuggestionsEnabled = false }).IsValid);
    }

    [Theory]
    [InlineData("notadomain")]
    [InlineData("user@example.com")]
    [InlineData("exa mple.com")]
    [InlineData("-bad.com")]
    public void Validate_Fails_When_PrimaryDomain_Is_Invalid_Whether_Enabled_Or_Not(string primary)
    {
        var enabled = _validator.Validate(ValidRequest() with { PrimaryDomain = primary });
        var disabled = _validator.Validate(ValidRequest() with { SuggestionsEnabled = false, PrimaryDomain = primary });

        Assert.Contains(enabled.Errors, e => e.PropertyName == nameof(UpdateWorkEmailSettingsRequest.PrimaryDomain));
        Assert.Contains(disabled.Errors, e => e.PropertyName == nameof(UpdateWorkEmailSettingsRequest.PrimaryDomain));
    }

    [Theory]
    [InlineData("@Example.COM")]
    [InlineData("  example.co.uk ")]
    public void Validate_Passes_When_PrimaryDomain_Needs_Normalisation(string primary)
    {
        Assert.True(_validator.Validate(ValidRequest() with { PrimaryDomain = primary }).IsValid);
    }
}

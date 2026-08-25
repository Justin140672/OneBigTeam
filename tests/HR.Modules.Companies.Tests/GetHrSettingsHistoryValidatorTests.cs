using HR.Modules.Companies.Features.GetHrSettingsHistory;

namespace HR.Modules.Companies.Tests;

public class GetHrSettingsHistoryValidatorTests
{
    private static GetHrSettingsHistoryRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        PageNumber = 1,
        PageSize = 20,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new GetHrSettingsHistoryValidator();
        Assert.True(validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new GetHrSettingsHistoryValidator();
        var result = validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetHrSettingsHistoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_PageNumber_Is_Zero()
    {
        var validator = new GetHrSettingsHistoryValidator();
        var result = validator.Validate(ValidRequest() with { PageNumber = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetHrSettingsHistoryRequest.PageNumber));
    }

    [Fact]
    public void Validate_Passes_When_PageSize_At_Lower_Boundary()
    {
        var validator = new GetHrSettingsHistoryValidator();
        var result = validator.Validate(ValidRequest() with { PageSize = 1 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Is_Zero()
    {
        var validator = new GetHrSettingsHistoryValidator();
        var result = validator.Validate(ValidRequest() with { PageSize = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetHrSettingsHistoryRequest.PageSize));
    }

    [Fact]
    public void Validate_Passes_When_PageSize_At_Upper_Boundary()
    {
        var validator = new GetHrSettingsHistoryValidator();
        var result = validator.Validate(ValidRequest() with { PageSize = 100 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Exceeds_Upper_Boundary()
    {
        var validator = new GetHrSettingsHistoryValidator();
        var result = validator.Validate(ValidRequest() with { PageSize = 101 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetHrSettingsHistoryRequest.PageSize));
    }
}

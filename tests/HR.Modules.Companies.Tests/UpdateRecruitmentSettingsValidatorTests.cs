using HR.Modules.Companies.Features.UpdateRecruitmentSettings;

namespace HR.Modules.Companies.Tests;

public class UpdateRecruitmentSettingsValidatorTests
{
    private static UpdateRecruitmentSettingsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        VacancyApprovalRequired = true,
        OfferApprovalRequired = false,
        CandidateRetentionDays = 730,
        Version = 1,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        Assert.True(validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRecruitmentSettingsRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_CandidateRetentionDays_Is_Below_Minimum()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { CandidateRetentionDays = 89 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRecruitmentSettingsRequest.CandidateRetentionDays));
    }

    [Fact]
    public void Validate_Passes_When_CandidateRetentionDays_At_Minimum_Boundary()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { CandidateRetentionDays = 90 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_CandidateRetentionDays_At_Maximum_Boundary()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { CandidateRetentionDays = 3650 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CandidateRetentionDays_Exceeds_Maximum()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { CandidateRetentionDays = 3651 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRecruitmentSettingsRequest.CandidateRetentionDays));
    }

    [Fact]
    public void Validate_Fails_When_Version_Is_Zero()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { Version = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRecruitmentSettingsRequest.Version));
    }

    [Fact]
    public void Validate_Fails_When_Version_Is_Negative()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { Version = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRecruitmentSettingsRequest.Version));
    }

    [Fact]
    public void Validate_Passes_When_Version_Is_One()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { Version = 1 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Both_Approval_Flags_Are_False()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { VacancyApprovalRequired = false, OfferApprovalRequired = false });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Both_Approval_Flags_Are_True()
    {
        var validator = new UpdateRecruitmentSettingsValidator();
        var result = validator.Validate(ValidRequest() with { VacancyApprovalRequired = true, OfferApprovalRequired = true });
        Assert.True(result.IsValid);
    }
}

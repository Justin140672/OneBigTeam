using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CompanySettingsRecruitmentSettingsTests
{
    [Fact]
    public void CreateDefault_Sets_Default_RecruitmentSettings()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.False(settings.VacancyApprovalRequired);
        Assert.False(settings.OfferApprovalRequired);
        Assert.Equal(730, settings.CandidateRetentionDays);
    }

    [Fact]
    public void UpdateRecruitmentSettings_Sets_New_Values()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateRecruitmentSettings(true, true, 365, DateTimeOffset.UtcNow);

        Assert.True(settings.VacancyApprovalRequired);
        Assert.True(settings.OfferApprovalRequired);
        Assert.Equal(365, settings.CandidateRetentionDays);
    }

    [Fact]
    public void UpdateRecruitmentSettings_Sets_Only_VacancyApprovalRequired_When_OfferApprovalRequired_Is_False()
    {
        // Covers the negated branch of each independent bool flag.
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateRecruitmentSettings(true, false, 730, DateTimeOffset.UtcNow);

        Assert.True(settings.VacancyApprovalRequired);
        Assert.False(settings.OfferApprovalRequired);
    }

    [Fact]
    public void UpdateRecruitmentSettings_Sets_Only_OfferApprovalRequired_When_VacancyApprovalRequired_Is_False()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateRecruitmentSettings(false, true, 730, DateTimeOffset.UtcNow);

        Assert.False(settings.VacancyApprovalRequired);
        Assert.True(settings.OfferApprovalRequired);
    }

    [Fact]
    public void UpdateRecruitmentSettings_Updates_UpdatedAt_And_Bumps_Version()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), createdAt);
        var versionBefore = settings.Version;
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        settings.UpdateRecruitmentSettings(true, false, 400, updatedAt);

        Assert.Equal(updatedAt, settings.UpdatedAt);
        Assert.Equal(versionBefore + 1, settings.Version);
    }
}

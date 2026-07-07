using HR.Modules.Onboarding.Domain;

namespace HR.Modules.Onboarding.Tests;

public class OnboardingTaskTemplateTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_All_Properties()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var template = OnboardingTaskTemplate.Create(
            id, companyId, "Set up workstation", "Provision laptop and accounts.", 1, FixedNow);

        Assert.Equal(id, template.Id);
        Assert.Equal(companyId, template.CompanyId);
        Assert.Equal("Set up workstation", template.Title);
        Assert.Equal("Provision laptop and accounts.", template.Description);
        Assert.Equal(1, template.DefaultDueDayOffset);
        Assert.Equal(FixedNow, template.CreatedAt);
        Assert.Equal(FixedNow, template.UpdatedAt);
    }

    [Fact]
    public void Create_Allows_Null_Description_And_DefaultDueDayOffset()
    {
        var template = OnboardingTaskTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Sign paperwork", null, null, FixedNow);

        Assert.Null(template.Description);
        Assert.Null(template.DefaultDueDayOffset);
    }

    [Fact]
    public void Update_Changes_Title_Description_DueDayOffset_And_Timestamp()
    {
        var template = OnboardingTaskTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Original title", "Original description.", 1, FixedNow);
        var later = FixedNow.AddDays(1);

        template.Update("Updated title", "Updated description.", 5, later);

        Assert.Equal("Updated title", template.Title);
        Assert.Equal("Updated description.", template.Description);
        Assert.Equal(5, template.DefaultDueDayOffset);
        Assert.Equal(later, template.UpdatedAt);
    }

    [Fact]
    public void Update_Allows_Clearing_Description_And_DefaultDueDayOffset()
    {
        var template = OnboardingTaskTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Original title", "Original description.", 1, FixedNow);

        template.Update("Original title", null, null, FixedNow.AddDays(1));

        Assert.Null(template.Description);
        Assert.Null(template.DefaultDueDayOffset);
    }
}

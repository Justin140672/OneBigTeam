using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Tests;

public class OnboardingDbContextTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Saves_And_Retrieves_OnboardingPlan()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), "Welcome aboard.", FixedNow);

        context.OnboardingPlans.Add(plan);
        await context.SaveChangesAsync();

        var saved = await context.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(employeeId, saved.EmployeeId);
        Assert.Equal(OnboardingStatus.NotStarted, saved.Status);
        Assert.Equal("Welcome aboard.", saved.Notes);
    }

    [Fact]
    public async Task Persists_OnboardingPlan_Status_Transitions()
    {
        await using var context = BuildContext();
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), null, FixedNow);
        context.OnboardingPlans.Add(plan);
        await context.SaveChangesAsync();

        plan.Start(FixedNow.AddDays(1));
        await context.SaveChangesAsync();

        var reloaded = await context.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.InProgress, reloaded.Status);
    }

    [Fact]
    public async Task Saves_And_Retrieves_OnboardingTaskTemplate()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var template = OnboardingTaskTemplate.Create(
            Guid.NewGuid(), companyId, "Set up workstation", "Provision laptop and accounts.", 1, FixedNow);

        context.OnboardingTaskTemplates.Add(template);
        await context.SaveChangesAsync();

        var saved = await context.OnboardingTaskTemplates.SingleAsync(t => t.Id == template.Id);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal("Set up workstation", saved.Title);
        Assert.Equal("Provision laptop and accounts.", saved.Description);
        Assert.Equal(1, saved.DefaultDueDayOffset);
    }

    [Fact]
    public void Model_Uses_Onboarding_Schema()
    {
        using var context = BuildContext();

        Assert.Equal("onboarding", context.Model.GetDefaultSchema());
    }

    private static OnboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}

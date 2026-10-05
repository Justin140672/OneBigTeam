using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class OnboardingTemplateReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static OnboardingTemplateReader BuildReader(EmployeesDbContext context) =>
        new(context, new OnboardingTemplateSeeder(context), new FakeClock(Now.UtcDateTime));

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PositionProfile BuildProfile(Guid companyId, Guid? templateId)
    {
        var profile = PositionProfile.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer",
            null, null, null, null, null, null, Guid.NewGuid(), Now);
        profile.Update(
            profile.DepartmentId, profile.LocationId, "Engineer", null, null, null, null, null, null,
            profile.DefaultLeavePolicyId, Now, templateId);
        return profile;
    }

    [Fact]
    public async Task GetOnboardingTemplateIdForPositionProfileAsync_Returns_Linked_Active_Template()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var profile = BuildProfile(companyId, template.Id);
        context.OnboardingTemplates.Add(template);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var result = await BuildReader(context)
            .GetOnboardingTemplateIdForPositionProfileAsync(companyId, profile.Id, CancellationToken.None);

        Assert.Equal(template.Id, result);
    }

    [Fact]
    public async Task GetOnboardingTemplateIdForPositionProfileAsync_Returns_Null_When_Linked_Template_Is_Inactive()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        template.Deactivate(Now);
        var profile = BuildProfile(companyId, template.Id);
        context.OnboardingTemplates.Add(template);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var result = await BuildReader(context)
            .GetOnboardingTemplateIdForPositionProfileAsync(companyId, profile.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOnboardingTemplateIdForPositionProfileAsync_Returns_Null_When_Profile_Has_No_Template()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var profile = BuildProfile(companyId, null);
        context.PositionProfiles.Add(profile);
        await context.SaveChangesAsync();

        var result = await BuildReader(context)
            .GetOnboardingTemplateIdForPositionProfileAsync(companyId, profile.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDefaultOnboardingTemplateIdAsync_Returns_Company_Default()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var other = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Other", null, Now);
        var defaultTemplate = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Custom Default", null, Now, isDefault: true);
        context.OnboardingTemplates.AddRange(other, defaultTemplate);
        await context.SaveChangesAsync();

        var result = await BuildReader(context).GetDefaultOnboardingTemplateIdAsync(companyId, CancellationToken.None);

        Assert.Equal(defaultTemplate.Id, result);
    }

    [Fact]
    public async Task GetDefaultOnboardingTemplateIdAsync_Seeds_Standard_Template_For_Company_Without_Templates()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var result = await BuildReader(context).GetDefaultOnboardingTemplateIdAsync(companyId, CancellationToken.None);

        Assert.NotNull(result);
        var template = await context.OnboardingTemplates.Include(t => t.Tasks).SingleAsync(t => t.CompanyId == companyId);
        Assert.Equal(template.Id, result);
        Assert.Equal("Standard Onboarding", template.Name);
        Assert.Equal(15, template.Tasks.Count);
    }

    [Fact]
    public async Task GetDefaultOnboardingTemplateIdAsync_Returns_Null_When_Company_Has_Templates_But_No_Active_Default()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var inactiveDefault = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Old Default", null, Now, isDefault: true);
        inactiveDefault.Deactivate(Now);
        context.OnboardingTemplates.Add(inactiveDefault);
        await context.SaveChangesAsync();

        var result = await BuildReader(context).GetDefaultOnboardingTemplateIdAsync(companyId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDefaultOnboardingTemplateIdAsync_Does_Not_Return_Another_Companys_Default()
    {
        await using var context = BuildContext();
        var otherCompanyId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        context.OnboardingTemplates.Add(
            OnboardingTemplate.Create(Guid.NewGuid(), otherCompanyId, "Other Default", null, Now, isDefault: true));
        context.OnboardingTemplates.Add(
            OnboardingTemplate.Create(Guid.NewGuid(), companyId, "Mine", null, Now));
        await context.SaveChangesAsync();

        var result = await BuildReader(context).GetDefaultOnboardingTemplateIdAsync(companyId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveTasksAsync_Returns_Active_Tasks_In_Order_Preserving_Hr_Owner()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var template = OnboardingTemplate.Create(Guid.NewGuid(), companyId, "T", null, Now);
        template.AddTask(Guid.NewGuid(), "Second", "d2", TaskPriority.High, OnboardingTemplateTaskAssignTo.Hr, 4, 2, Now);
        template.AddTask(Guid.NewGuid(), "First", "d1", TaskPriority.Low, OnboardingTemplateTaskAssignTo.Manager, 1, 1, Now);
        var removed = template.AddTask(Guid.NewGuid(), "Removed", null, TaskPriority.Low, OnboardingTemplateTaskAssignTo.NewHire, 0, 3, Now);
        removed.Deactivate();
        context.OnboardingTemplates.Add(template);
        await context.SaveChangesAsync();

        var tasks = await BuildReader(context).GetActiveTasksAsync(companyId, template.Id, CancellationToken.None);

        Assert.Equal(["First", "Second"], tasks.Select(t => t.Title));
        var hrTask = Assert.Single(tasks, t => t.Title == "Second");
        Assert.Equal(OnboardingTemplateTaskAssignTo.Hr, hrTask.AssignTo);
        Assert.Equal("d2", hrTask.Description);
        Assert.Equal(TaskPriority.High, hrTask.Priority);
        Assert.Equal(4, hrTask.DueDaysAfterStart);
    }
}

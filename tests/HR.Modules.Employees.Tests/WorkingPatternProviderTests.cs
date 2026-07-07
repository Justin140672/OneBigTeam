using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class WorkingPatternProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetEffectivePatternAsync_Returns_Company_Default_When_No_Overrides()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2026, 1, 1), true, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var companyDefault = new WorkingPattern(WorkingDays.Monday | WorkingDays.Tuesday, 6m);
        var provider = new WorkingPatternProvider(context, new FakeCompanyLeaveSettingsReader(
            new CompanyLeaveSettings(true, 1, 25, companyDefault)));

        var result = await provider.GetEffectivePatternAsync(companyId, employee.Id, CancellationToken.None);

        Assert.Equal(companyDefault, result);
    }

    [Fact]
    public async Task GetEffectivePatternAsync_Prefers_Employee_Override_Over_PositionProfile_And_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(
            Guid.NewGuid(), companyId, null, null, "Engineer", null, null,
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday, 6m, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2026, 1, 1), true, Now);
        employee.Assign(null, profile.Id, null, null, Now);
        employee.SetWorkingPattern(WorkingDays.Friday, 4m, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var provider = new WorkingPatternProvider(context, new FakeCompanyLeaveSettingsReader());

        var result = await provider.GetEffectivePatternAsync(companyId, employee.Id, CancellationToken.None);

        Assert.Equal(new WorkingPattern(WorkingDays.Friday, 4m), result);
    }

    [Fact]
    public async Task GetEffectivePatternAsync_Falls_Back_To_PositionProfile_When_No_Employee_Override()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday;
        var profile = PositionProfile.Create(
            Guid.NewGuid(), companyId, null, null, "Engineer", null, null,
            profileDays, 6m, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2026, 1, 1), true, Now);
        employee.Assign(null, profile.Id, null, null, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var companyDefault = new WorkingPattern(WorkingDays.Monday, 8m);
        var provider = new WorkingPatternProvider(context, new FakeCompanyLeaveSettingsReader(
            new CompanyLeaveSettings(true, 1, 25, companyDefault)));

        var result = await provider.GetEffectivePatternAsync(companyId, employee.Id, CancellationToken.None);

        Assert.Equal(new WorkingPattern(profileDays, 6m), result);
    }

    [Fact]
    public async Task GetEffectivePatternAsync_Falls_Back_To_Company_When_PositionProfile_Has_No_Override()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profile = PositionProfile.Create(Guid.NewGuid(), companyId, null, null, "Engineer", null, null, null, null, null, null, null, null, Now);
        context.PositionProfiles.Add(profile);

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", new DateOnly(2026, 1, 1), true, Now);
        employee.Assign(null, profile.Id, null, null, Now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var companyDefault = new WorkingPattern(WorkingDays.Monday, 8m);
        var provider = new WorkingPatternProvider(context, new FakeCompanyLeaveSettingsReader(
            new CompanyLeaveSettings(true, 1, 25, companyDefault)));

        var result = await provider.GetEffectivePatternAsync(companyId, employee.Id, CancellationToken.None);

        Assert.Equal(companyDefault, result);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}

using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services.OnboardingTasks;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class ConfigureHrSettingsTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Settings_Never_Edited()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var settings = CompanySettings.CreateDefault(companyId, Now);
        context.CompanySettings.Add(settings);
        await context.SaveChangesAsync();

        var task = new ConfigureHrSettingsTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_When_Settings_Updated_After_Creation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var settings = CompanySettings.CreateDefault(companyId, Now);
        settings.UpdateCompanyProfile("Europe/London", "en-GB", Now.AddDays(1));
        context.CompanySettings.Add(settings);
        await context.SaveChangesAsync();

        var task = new ConfigureHrSettingsTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_No_Settings_Row_Exists()
    {
        await using var context = BuildContext();

        var task = new ConfigureHrSettingsTask(context);

        var result = await task.IsCompletedAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CompaniesDbContext(options);
    }
}

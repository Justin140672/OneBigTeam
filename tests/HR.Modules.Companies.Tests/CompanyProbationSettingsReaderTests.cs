using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CompanyProbationSettingsReaderTests
{
    [Fact]
    public async Task GetCheckpointDaysAsync_Returns_Default_When_No_Settings_Row_Exists()
    {
        await using var context = BuildContext();
        var reader = new CompanyProbationSettingsReader(context);

        var checkpointDays = await reader.GetCheckpointDaysAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(CompanyProbationSettings.DefaultCheckpointDays, checkpointDays);
    }

    [Fact]
    public async Task GetCheckpointDaysAsync_Returns_Configured_Days_Sorted_When_All_Configured()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateProbationCheckpoints(90, 14, 45, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var reader = new CompanyProbationSettingsReader(context);

        var checkpointDays = await reader.GetCheckpointDaysAsync(company.Id, CancellationToken.None);

        Assert.Equal([14, 45, 90], checkpointDays);
    }

    [Fact]
    public async Task GetCheckpointDaysAsync_Returns_Only_NonNull_Days_When_Partially_Configured()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateProbationCheckpoints(20, null, null, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var reader = new CompanyProbationSettingsReader(context);

        var checkpointDays = await reader.GetCheckpointDaysAsync(company.Id, CancellationToken.None);

        Assert.Equal([20], checkpointDays);
    }

    [Fact]
    public async Task GetCheckpointDaysAsync_Falls_Back_To_Default_When_All_Configured_Days_Are_Null()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateProbationCheckpoints(null, null, null, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var reader = new CompanyProbationSettingsReader(context);

        var checkpointDays = await reader.GetCheckpointDaysAsync(company.Id, CancellationToken.None);

        Assert.Equal(CompanyProbationSettings.DefaultCheckpointDays, checkpointDays);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}

using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class StagingCompanySeederWorkEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeedAsync_Sets_WorkEmail_Settings_On_A_New_Company()
    {
        await using var context = BuildContext();
        var options = new StagingSeedOptions { Enabled = true, EmailDomain = "@Demo.Example" };

        await StagingCompanySeeder.SeedAsync(context, options, Now);

        var settings = await context.CompanySettings.SingleAsync(s => s.CompanyId == StagingSeedOptions.CompanyId);
        Assert.True(settings.WorkEmailSuggestionsEnabled);
        Assert.Equal("demo.example", settings.WorkEmailPrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstNameDotLastName, settings.WorkEmailNamingConvention);
    }

    [Fact]
    public async Task SeedAsync_Uses_The_Default_Staging_Domain_When_None_Is_Configured()
    {
        await using var context = BuildContext();

        await StagingCompanySeeder.SeedAsync(context, new StagingSeedOptions { Enabled = true }, Now);

        var settings = await context.CompanySettings.SingleAsync(s => s.CompanyId == StagingSeedOptions.CompanyId);
        Assert.Equal("staging.example", settings.WorkEmailPrimaryDomain);
    }

    [Fact]
    public async Task SeedAsync_Backfills_An_Existing_Company_That_Has_No_Primary_Domain()
    {
        await using var context = BuildContext();
        await AddExistingCompanyAsync(context, domain: null);

        await StagingCompanySeeder.SeedAsync(context, new StagingSeedOptions { Enabled = true, EmailDomain = "demo.example" }, Now);

        var settings = await context.CompanySettings.SingleAsync(s => s.CompanyId == StagingSeedOptions.CompanyId);
        Assert.True(settings.WorkEmailSuggestionsEnabled);
        Assert.Equal("demo.example", settings.WorkEmailPrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstNameDotLastName, settings.WorkEmailNamingConvention);
    }

    [Fact]
    public async Task SeedAsync_Does_Not_Overwrite_Configured_WorkEmail_Settings_On_An_Existing_Company()
    {
        await using var context = BuildContext();
        await AddExistingCompanyAsync(context, domain: "custom.example");
        var versionBefore = (await context.CompanySettings.SingleAsync()).Version;

        await StagingCompanySeeder.SeedAsync(context, new StagingSeedOptions { Enabled = true, EmailDomain = "demo.example" }, Now);

        var settings = await context.CompanySettings.SingleAsync(s => s.CompanyId == StagingSeedOptions.CompanyId);
        Assert.Equal("custom.example", settings.WorkEmailPrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstName, settings.WorkEmailNamingConvention);
        Assert.False(settings.WorkEmailSuggestionsEnabled);
        Assert.Equal(versionBefore, settings.Version);
    }

    [Fact]
    public async Task SeedAsync_Is_Idempotent_When_Run_Twice()
    {
        await using var context = BuildContext();
        var options = new StagingSeedOptions { Enabled = true, EmailDomain = "demo.example" };

        await StagingCompanySeeder.SeedAsync(context, options, Now);
        var versionAfterFirst = (await context.CompanySettings.SingleAsync()).Version;
        await StagingCompanySeeder.SeedAsync(context, options, Now);

        var settings = await context.CompanySettings.SingleAsync();
        Assert.Equal("demo.example", settings.WorkEmailPrimaryDomain);
        Assert.Equal(versionAfterFirst, settings.Version);
    }

    private static async Task AddExistingCompanyAsync(CompaniesDbContext context, string? domain)
    {
        var company = Company.Create(StagingSeedOptions.CompanyId, "Staging Demo Ltd", Now);
        var settings = CompanySettings.CreateDefault(company.Id, Now);
        if (domain is not null)
            settings.UpdateWorkEmailSettings(false, domain, WorkEmailNamingConvention.FirstName, Now);
        company.SetSettings(settings, Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}

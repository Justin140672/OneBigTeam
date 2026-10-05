using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetWorkEmailSettings;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class GetWorkEmailSettingsHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await new GetWorkEmailSettingsHandler(context)
            .HandleAsync(new GetWorkEmailSettingsRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Defaults_When_Company_Has_No_Settings_Row()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var result = await new GetWorkEmailSettingsHandler(context)
            .HandleAsync(new GetWorkEmailSettingsRequest { CompanyId = company.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(company.Id, result.Value!.CompanyId);
        Assert.True(result.Value.SuggestionsEnabled);
        Assert.Null(result.Value.PrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstNameDotLastName, result.Value.NamingConvention);
        Assert.Equal(1, result.Value.Version);
    }

    [Fact]
    public async Task HandleAsync_Returns_Configured_Values()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateWorkEmailSettings(true, "example.com", WorkEmailNamingConvention.FirstNameLastName, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var result = await new GetWorkEmailSettingsHandler(context)
            .HandleAsync(new GetWorkEmailSettingsRequest { CompanyId = company.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.SuggestionsEnabled);
        Assert.Equal("example.com", result.Value.PrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstNameLastName, result.Value.NamingConvention);
        Assert.Equal(2, result.Value.Version);
    }

    [Fact]
    public async Task HandleAsync_Returns_An_Example_For_Every_Naming_Convention()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Acme", DateTimeOffset.UtcNow);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var result = await new GetWorkEmailSettingsHandler(context)
            .HandleAsync(new GetWorkEmailSettingsRequest { CompanyId = company.Id }, CancellationToken.None);

        Assert.Equal("Jane", result.Value!.ExampleFirstName);
        Assert.Equal("Smith", result.Value.ExampleLastName);
        Assert.Equal(4, result.Value.Examples.Count);
        Assert.Equal("jane.smith", result.Value.Examples.Single(e => e.Convention == WorkEmailNamingConvention.FirstNameDotLastName).LocalPart);
        Assert.Equal("j.smith", result.Value.Examples.Single(e => e.Convention == WorkEmailNamingConvention.FirstInitialDotLastName).LocalPart);
        Assert.Equal("janesmith", result.Value.Examples.Single(e => e.Convention == WorkEmailNamingConvention.FirstNameLastName).LocalPart);
        Assert.Equal("jane", result.Value.Examples.Single(e => e.Convention == WorkEmailNamingConvention.FirstName).LocalPart);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}

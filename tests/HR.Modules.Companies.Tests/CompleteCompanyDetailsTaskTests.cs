using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services.OnboardingTasks;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class CompleteCompanyDetailsTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Company_Has_No_Addresses()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var task = new CompleteCompanyDetailsTask(context);

        var result = await task.IsCompletedAsync(company.Id, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Company_Name_Is_Blank()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), " ", Now);
        var address = CompanyAddress.Create(
            Guid.NewGuid(), company.Id, CompanyAddressType.RegisteredOffice,
            "10 High Street", null, "London", null, "SW1A 1AA", "GB", Now);
        company.SetAddress(address, Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var task = new CompleteCompanyDetailsTask(context);

        var result = await task.IsCompletedAsync(company.Id, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_When_Company_Has_Valid_Address_And_Name()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", Now);
        var address = CompanyAddress.Create(
            Guid.NewGuid(), company.Id, CompanyAddressType.RegisteredOffice,
            "10 High Street", null, "London", null, "SW1A 1AA", "GB", Now);
        company.SetAddress(address, Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var task = new CompleteCompanyDetailsTask(context);

        var result = await task.IsCompletedAsync(company.Id, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Address_Missing_Line1_Or_PostalCode()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Acme Corporation", Now);
        var address = CompanyAddress.Create(
            Guid.NewGuid(), company.Id, CompanyAddressType.RegisteredOffice,
            "", null, "London", null, null, "GB", Now);
        company.SetAddress(address, Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var task = new CompleteCompanyDetailsTask(context);

        var result = await task.IsCompletedAsync(company.Id, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var task = new CompleteCompanyDetailsTask(context);

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

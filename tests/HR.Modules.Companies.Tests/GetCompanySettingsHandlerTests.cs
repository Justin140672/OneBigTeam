using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetCompanySettings;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class GetCompanySettingsHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_Customised_Settings_When_They_Exist()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        settings.Update(
            "Europe/London", "en-GB", WorkingDays.Monday | WorkingDays.Tuesday, 6m, 4, 30, 8,
            false, true, true, 7, 3, "Custom acknowledgement statement.", 5, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetCompanySettingsHandler(context);

        var result = await handler.HandleAsync(new GetCompanySettingsRequest { Id = company.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Europe/London", result.Value!.TimeZone);
        Assert.Equal(4, result.Value.LeaveYearStartMonth);
        Assert.Equal(30, result.Value.DefaultHolidayAllowance);
        Assert.True(result.Value.ExcludePublicHolidaysFromSickness);
        Assert.True(result.Value.DisplaySalaryOnEmployeeProfile);
        Assert.Equal(7, result.Value.FitNoteRequiredAfterDays);
        Assert.Equal("Custom acknowledgement statement.", result.Value.DefaultAcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Returns_Defaults_When_Company_Exists_But_Has_No_Customised_Settings()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now); // no SetSettings call
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetCompanySettingsHandler(context);

        var result = await handler.HandleAsync(new GetCompanySettingsRequest { Id = company.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(company.Id, result.Value!.CompanyId);
        Assert.Equal("UTC", result.Value.TimeZone);
        Assert.Equal("en-GB", result.Value.Locale);
        Assert.Equal(1, result.Value.LeaveYearStartMonth);
        Assert.Equal(25, result.Value.DefaultHolidayAllowance);
        Assert.False(result.Value.DisplaySalaryOnEmployeeProfile);
        Assert.False(string.IsNullOrEmpty(result.Value.PostcodeRegex));
        Assert.Equal(CompanySettings.DefaultAcknowledgementStatementText, result.Value.DefaultAcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetCompanySettingsHandler(context);

        var result = await handler.HandleAsync(new GetCompanySettingsRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}

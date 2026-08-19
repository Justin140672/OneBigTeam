using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetHrSettings;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class GetHrSettingsHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_Customised_Settings_When_They_Exist()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateCompanyProfile("Europe/London", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday, 6m, 4, 30, 8,
            false, true, true, 7, 3, "Custom acknowledgement statement.", 5,
            NoticePeriodUnit.Weeks, 2, false, EmployeeNumberMode.Manual, null, 1, 1, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetHrSettingsHandler(context);

        var result = await handler.HandleAsync(new GetHrSettingsRequest { CompanyId = company.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(company.Id, result.Value!.CompanyId);
        Assert.Equal((int)(WorkingDays.Monday | WorkingDays.Tuesday), result.Value.WorkingDays);
        Assert.Equal(6m, result.Value.HoursPerDay);
        Assert.Equal(4, result.Value.LeaveYearStartMonth);
        Assert.Equal(30, result.Value.DefaultHolidayAllowance);
        Assert.Equal(8, result.Value.ProbationMonths);
        Assert.False(result.Value.ExcludePublicHolidaysFromLeave);
        Assert.True(result.Value.ExcludePublicHolidaysFromSickness);
        Assert.True(result.Value.DisplaySalaryOnEmployeeProfile);
        Assert.Equal(7, result.Value.FitNoteRequiredAfterDays);
        Assert.Equal(3, result.Value.ReturnToWorkRequiredAfterDays);
        Assert.Equal("Custom acknowledgement statement.", result.Value.DefaultAcknowledgementStatement);
        Assert.Equal(5, result.Value.AcknowledgementReminderIntervalDays);
        Assert.Equal(NoticePeriodUnit.Weeks, result.Value.NoticePeriodUnit);
        Assert.Equal(2, result.Value.NoticePeriodLength);
        Assert.False(result.Value.AutoDisableAccessOnLeavingDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_Defaults_When_Company_Exists_But_Has_No_Customised_Settings()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now); // no SetSettings call
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetHrSettingsHandler(context);

        var result = await handler.HandleAsync(new GetHrSettingsRequest { CompanyId = company.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(company.Id, result.Value!.CompanyId);
        Assert.Equal(1, result.Value.LeaveYearStartMonth);
        Assert.Equal(25, result.Value.DefaultHolidayAllowance);
        Assert.False(result.Value.DisplaySalaryOnEmployeeProfile);
        Assert.Equal(CompanySettings.DefaultAcknowledgementStatementText, result.Value.DefaultAcknowledgementStatement);
        Assert.Equal(NoticePeriodUnit.Months, result.Value.NoticePeriodUnit);
        Assert.Equal(1, result.Value.NoticePeriodLength);
        Assert.True(result.Value.AutoDisableAccessOnLeavingDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_Configured_EmployeeNumberSettings_When_Settings_Row_Exists()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now);
        var settings = CompanySettings.CreateDefault(company.Id, now);
        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, 7, 1,
            "Custom statement.", 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, "EMP-", 125, 5, now);
        company.SetSettings(settings, now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetHrSettingsHandler(context);

        var result = await handler.HandleAsync(new GetHrSettingsRequest { CompanyId = company.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeNumberMode.Automatic, result.Value!.EmployeeNumberMode);
        Assert.Equal("EMP-", result.Value.EmployeeNumberPrefix);
        Assert.Equal(125, result.Value.NextEmployeeNumber);
        Assert.Equal(5, result.Value.EmployeeNumberMinimumLength);
    }

    [Fact]
    public async Task HandleAsync_Returns_Default_EmployeeNumberSettings_When_No_Customised_Settings_Exist()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var company = Company.Create(Guid.NewGuid(), "Acme", now); // no SetSettings call
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetHrSettingsHandler(context);

        var result = await handler.HandleAsync(new GetHrSettingsRequest { CompanyId = company.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeNumberMode.Automatic, result.Value!.EmployeeNumberMode);
        Assert.Null(result.Value.EmployeeNumberPrefix);
        Assert.Equal(1, result.Value.NextEmployeeNumber);
        Assert.Equal(4, result.Value.EmployeeNumberMinimumLength);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetHrSettingsHandler(context);

        var result = await handler.HandleAsync(new GetHrSettingsRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

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

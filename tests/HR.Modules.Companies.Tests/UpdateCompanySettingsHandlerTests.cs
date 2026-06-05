using System.Text.Json;
using HR.Modules.Companies.Contracts.Events;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Tests;

public class UpdateCompanySettingsHandlerTests
{
    [Fact]
    public async Task HandleAsync_Updates_Settings_And_Writes_Outbox_Event()
    {
        await using var context = BuildContext();
        var createdAt = new DateTimeOffset(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var company = Company.Create(Guid.NewGuid(), "Acme", "acme", createdAt);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, createdAt), createdAt);

        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanySettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
            new FakeCurrentUser { UserId = Guid.Parse("1ac2572d-1e78-4b7f-90a8-f9e2f806410e") });

        var result = await handler.HandleAsync(
            new UpdateCompanySettingsRequest
            {
                Id = company.Id,
                TimeZone = " Europe/London ",
                Locale = " en-GB ",
                WorkingWeek = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday,
                LeaveYearStartMonth = 4,
                DefaultHolidayAllowance = 28.5m,
                ProbationMonths = 3
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(company.Id, result.Value!.CompanyId);
        Assert.Equal("Europe/London", result.Value.TimeZone);
        Assert.Equal(28.5m, result.Value.DefaultHolidayAllowance);

        var savedCompany = await context.Companies
            .Include(currentCompany => currentCompany.Settings)
            .SingleAsync(currentCompany => currentCompany.Id == company.Id);

        Assert.NotNull(savedCompany.Settings);
        Assert.Equal("Europe/London", savedCompany.Settings!.TimeZone);
        Assert.Equal(4, savedCompany.Settings.LeaveYearStartMonth);
        Assert.Equal(3, savedCompany.Settings.ProbationMonths);

        var outboxMessage = await context.OutboxMessages.SingleAsync();
        Assert.Equal(company.Id, outboxMessage.CompanyId);
        Assert.Equal(nameof(CompanySettingsUpdatedIntegrationEvent), outboxMessage.EventType);
        Assert.Equal("pending", outboxMessage.Status);

        var integrationEvent = JsonSerializer.Deserialize<CompanySettingsUpdatedIntegrationEvent>(outboxMessage.Payload);
        Assert.NotNull(integrationEvent);
        Assert.Equal(company.Id, integrationEvent!.CompanyId);
        Assert.Equal(Guid.Parse("1ac2572d-1e78-4b7f-90a8-f9e2f806410e"), integrationEvent.PerformedByUserId);
        Assert.Equal("UTC", integrationEvent.Previous.TimeZone);
        Assert.Equal("Europe/London", integrationEvent.Current.TimeZone);
        Assert.Equal(
            WorkingDays.Monday
            | WorkingDays.Tuesday
            | WorkingDays.Wednesday
            | WorkingDays.Thursday
            | WorkingDays.Friday,
            integrationEvent.Previous.WorkingWeek);
        Assert.Equal(
            WorkingDays.Monday
            | WorkingDays.Tuesday
            | WorkingDays.Wednesday,
            integrationEvent.Current.WorkingWeek);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Company_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new UpdateCompanySettingsHandler(
            context,
            new FakeClock(new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)),
            new FakeCurrentUser { UserId = Guid.NewGuid() });

        var result = await handler.HandleAsync(
            new UpdateCompanySettingsRequest
            {
                Id = Guid.NewGuid(),
                TimeZone = "UTC",
                Locale = "en-GB",
                WorkingWeek = WorkingDays.Monday | WorkingDays.Tuesday,
                LeaveYearStartMonth = 1,
                DefaultHolidayAllowance = 25m,
                ProbationMonths = 6
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(context.OutboxMessages);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}

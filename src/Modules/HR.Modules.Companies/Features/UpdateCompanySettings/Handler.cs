using System.Text.Json;
using HR.Modules.Companies.Contracts.Events;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed class UpdateCompanySettingsHandler
{
    private readonly CompaniesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public UpdateCompanySettingsHandler(
        CompaniesDbContext dbContext,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<UpdateCompanySettingsResponse>> HandleAsync(
        UpdateCompanySettingsRequest request,
        CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .Include(currentCompany => currentCompany.Settings)
            .SingleOrDefaultAsync(currentCompany => currentCompany.Id == request.Id, cancellationToken);

        if (company is null)
        {
            return Result.Failure<UpdateCompanySettingsResponse>(
                Error.NotFound($"Company with id '{request.Id}' was not found."));
        }

        var utcNow = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var now = new DateTimeOffset(utcNow);

        if (company.Settings is null)
        {
            company.SetSettings(CompanySettings.CreateDefault(company.Id, now), now);
        }

        var settings = company.Settings!;
        var previousSettings = new CompanySettingsEventSnapshot(
            settings.TimeZone,
            settings.Locale,
            settings.WorkingWeek,
            settings.LeaveYearStartMonth,
            settings.DefaultHolidayAllowance,
            settings.ProbationMonths);

        settings.Update(
            request.TimeZone.Trim(),
            request.Locale.Trim(),
            request.WorkingWeek,
            request.LeaveYearStartMonth,
            request.DefaultHolidayAllowance,
            request.ProbationMonths,
            now);

        company.SetSettings(settings, now);

        var integrationEvent = new CompanySettingsUpdatedIntegrationEvent(
            company.Id,
            _currentUser.UserId,
            now,
            previousSettings,
            new CompanySettingsEventSnapshot(
                settings.TimeZone,
                settings.Locale,
                settings.WorkingWeek,
                settings.LeaveYearStartMonth,
                settings.DefaultHolidayAllowance,
                settings.ProbationMonths));

        var payload = JsonSerializer.Serialize(integrationEvent);

        _dbContext.OutboxMessages.Add(
            OutboxMessage.Create(
                Guid.NewGuid(),
                company.Id,
                nameof(CompanySettingsUpdatedIntegrationEvent),
                payload,
                now));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new UpdateCompanySettingsResponse(
            company.Id,
            settings.TimeZone,
            settings.Locale,
            settings.WorkingWeek,
            settings.LeaveYearStartMonth,
            settings.DefaultHolidayAllowance,
            settings.ProbationMonths,
            settings.UpdatedAt);

        return Result.Success(response);
    }
}

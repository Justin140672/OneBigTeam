using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.CreatePublicHoliday;

internal sealed class CreatePublicHolidayHandler
{
    private readonly CompaniesDbContext _dbContext;
    private readonly IClock _clock;

    public CreatePublicHolidayHandler(CompaniesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreatePublicHolidayResponse>> HandleAsync(
        CreatePublicHolidayRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.PublicHolidays
            .AnyAsync(
                h => h.CompanyId == request.CompanyId && h.Date == request.Date,
                cancellationToken);

        if (exists)
        {
            return Result.Failure<CreatePublicHolidayResponse>(
                Error.Conflict($"A public holiday on {request.Date} already exists for this company."));
        }

        var now = _clock.UtcNowOffset();

        var holiday = PublicHoliday.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Date,
            request.Name.Trim(),
            request.CountryCode.Trim().ToUpperInvariant(),
            now);

        _dbContext.PublicHolidays.Add(holiday);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatePublicHolidayResponse(
            holiday.Id,
            holiday.CompanyId,
            holiday.Date,
            holiday.Name,
            holiday.CountryCode,
            holiday.CreatedAt));
    }
}

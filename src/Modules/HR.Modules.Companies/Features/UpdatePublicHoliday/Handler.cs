using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdatePublicHoliday;

internal sealed class UpdatePublicHolidayHandler
{
    private readonly CompaniesDbContext _dbContext;

    public UpdatePublicHolidayHandler(CompaniesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<UpdatePublicHolidayResponse>> HandleAsync(
        UpdatePublicHolidayRequest request,
        CancellationToken cancellationToken)
    {
        var holiday = await _dbContext.PublicHolidays
            .SingleOrDefaultAsync(
                h => h.Id == request.Id && h.CompanyId == request.CompanyId,
                cancellationToken);

        if (holiday is null)
        {
            return Result.Failure<UpdatePublicHolidayResponse>(
                Error.NotFound($"Public holiday '{request.Id}' was not found."));
        }

        if (holiday.Date != request.Date)
        {
            var dateConflict = await _dbContext.PublicHolidays
                .AnyAsync(
                    h => h.CompanyId == request.CompanyId && h.Date == request.Date && h.Id != request.Id,
                    cancellationToken);

            if (dateConflict)
            {
                return Result.Failure<UpdatePublicHolidayResponse>(
                    Error.Conflict($"A public holiday on {request.Date} already exists for this company."));
            }
        }

        holiday.Update(
            request.Date,
            request.Name.Trim(),
            request.CountryCode.Trim().ToUpperInvariant());

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdatePublicHolidayResponse(
            holiday.Id,
            holiday.CompanyId,
            holiday.Date,
            holiday.Name,
            holiday.CountryCode,
            holiday.CreatedAt));
    }
}

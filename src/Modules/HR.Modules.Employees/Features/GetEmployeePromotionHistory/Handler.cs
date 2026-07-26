using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployeePromotionHistory;

internal sealed class GetEmployeePromotionHistoryHandler(
    EmployeesDbContext dbContext,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<Result<GetEmployeePromotionHistoryResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == companyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<GetEmployeePromotionHistoryResponse>(
                Error.NotFound($"Employee '{employeeId}' was not found."));

        var promotions = await dbContext.EmployeePromotions
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.EffectiveDate)
            .Select(p => new
            {
                p.Id,
                p.PreviousPositionProfileId,
                p.NewPositionProfileId,
                p.EffectiveDate,
                p.Reason,
                p.Notes,
                p.CreatedBy,
                p.CreatedDate,
                p.CompletedAt,
            })
            .ToListAsync(cancellationToken);

        var positionProfileIds = promotions
            .Select(p => p.PreviousPositionProfileId)
            .Concat(promotions.Select(p => p.NewPositionProfileId))
            .Distinct()
            .ToList();

        var positionProfileTitles = positionProfileIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.PositionProfiles
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && positionProfileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

        var createdByNames = await employeeNameReader.GetNamesAsync(
            companyId, promotions.Select(p => p.CreatedBy).Distinct(), cancellationToken);

        var items = promotions
            .Select(p => new EmployeePromotionHistoryItem(
                p.Id,
                positionProfileTitles.TryGetValue(p.PreviousPositionProfileId, out var previousTitle) ? previousTitle : "Unknown",
                positionProfileTitles.TryGetValue(p.NewPositionProfileId, out var newTitle) ? newTitle : "Unknown",
                p.EffectiveDate,
                p.Reason,
                p.Notes,
                createdByNames.TryGetValue(p.CreatedBy, out var createdByName) ? createdByName : "Unknown",
                p.CreatedDate,
                p.CompletedAt))
            .ToList();

        return Result.Success(new GetEmployeePromotionHistoryResponse(items));
    }
}

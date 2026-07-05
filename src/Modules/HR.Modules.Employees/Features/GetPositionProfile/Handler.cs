using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetPositionProfile;

internal sealed class GetPositionProfileHandler
{
    private readonly EmployeesDbContext _dbContext;

    public GetPositionProfileHandler(EmployeesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetPositionProfileResponse>> HandleAsync(
        GetPositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await _dbContext.PositionProfiles
            .AsNoTracking()
            .Include(p => p.RequiredDocuments)
            .SingleOrDefaultAsync(
                p => p.Id == request.Id && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<GetPositionProfileResponse>(
                Error.NotFound($"Position profile with id '{request.Id}' was not found."));
        }

        var requiredDocuments = profile.RequiredDocuments
            .Where(d => d.IsActive)
            .Select(d => new RequiredDocumentItem(d.Id, d.DocumentTypeId, d.IsMandatory, d.DueDaysAfterStart, d.RequiresExpiryDate))
            .ToList();

        return Result.Success(new GetPositionProfileResponse(
            profile.Id,
            profile.CompanyId,
            profile.DepartmentId,
            profile.Title,
            profile.Description,
            profile.IsManagerial,
            profile.ProbationMonthsOverride,
            profile.WorkingDaysOverride,
            profile.HoursPerDayOverride,
            profile.SalaryMin,
            profile.SalaryMax,
            profile.DefaultLeavePolicyId,
            profile.IsActive,
            profile.CreatedAt,
            profile.UpdatedAt,
            requiredDocuments));
    }
}

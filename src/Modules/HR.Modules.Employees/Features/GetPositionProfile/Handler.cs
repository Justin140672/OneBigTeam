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
            .Include(p => p.RequiredAssets)
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

        var requiredAssets = profile.RequiredAssets
            .Where(a => a.IsActive)
            .Select(a => new RequiredAssetItem(a.Id, a.AssetCategoryId, a.IsMandatory, a.Quantity))
            .ToList();

        return Result.Success(new GetPositionProfileResponse(
            profile.Id,
            profile.CompanyId,
            profile.DepartmentId,
            profile.LocationId,
            profile.Title,
            profile.Description,
            profile.ProbationMonthsOverride,
            profile.WorkingDaysOverride,
            profile.HoursPerDayOverride,
            profile.NoticePeriodUnitOverride,
            profile.NoticePeriodLengthOverride,
            profile.SalaryMin,
            profile.SalaryMax,
            profile.SalaryType,
            profile.DefaultLeavePolicyId,
            profile.OnboardingTemplateId,
            profile.IsActive,
            profile.CreatedAt,
            profile.UpdatedAt,
            requiredDocuments,
            requiredAssets));
    }
}

using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListOnboardingTemplatesForPositionProfile;

internal sealed class ListOnboardingTemplatesForPositionProfileHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<ListOnboardingTemplatesForPositionProfileResponse>> HandleAsync(
        ListOnboardingTemplatesForPositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profileExists = await dbContext.PositionProfiles
            .AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (!profileExists)
            return Result.Failure<ListOnboardingTemplatesForPositionProfileResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var items = await dbContext.PositionProfileOnboardingTemplates
            .AsNoTracking()
            .Where(a => a.PositionProfileId == request.PositionProfileId
                     && a.CompanyId == request.CompanyId
                     && a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .Join(
                dbContext.OnboardingTemplates.AsNoTracking(),
                a => a.OnboardingTemplateId,
                t => t.Id,
                (a, t) => new PositionProfileOnboardingTemplateListItem(
                    a.Id,
                    t.Id,
                    t.Name,
                    t.Description,
                    t.Tasks.Count(task => task.IsActive)))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListOnboardingTemplatesForPositionProfileResponse(items));
    }
}

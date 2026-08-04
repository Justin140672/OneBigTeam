using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services.OnboardingTasks;

/// <summary>
/// Identity's ApplicationUser rows are keyed by the same id as the owning Employee (see
/// ListUsersHandler's employeeIds-first approach), and ApplicationUser itself carries no
/// CompanyId — company scoping is resolved via the cross-module IEmployeeAudienceReader
/// contract (implemented in HR.Modules.Employees), the same pattern ListUsersHandler already
/// uses via IEmployeeAudienceReader.GetAllEmployeeIdsAsync.
/// </summary>
internal sealed class InviteAdditionalUsersTask(
    IdentityDbContext dbContext,
    IEmployeeAudienceReader employeeAudienceReader) : IOnboardingTaskDefinition
{
    public string Key => "invite-additional-users";
    public string Name => "Invite your team";
    public string Description => "Invite additional users so others can access the system.";
    public bool IsMandatory => true;
    // HR.Web's user administration route is company-scoped
    // ("/companies/{CompanyId:guid}/user-administration") — the "{companyId}" placeholder is
    // substituted by HR.Web with the current company id.
    public string LinkUrl => "/companies/{companyId}/user-administration";
    public int Order => 5;

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var employeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(companyId, cancellationToken);

        var activeUserCount = await dbContext.Users
            .AsNoTracking()
            .CountAsync(u => employeeIds.Contains(u.Id) && u.IsActive, cancellationToken);

        return activeUserCount > 1;
    }
}

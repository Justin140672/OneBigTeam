using HR.Modules.Employees.Contracts;
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
    public string Description => "Invite employees so they can sign in — invited employees get access to the system under their own account.";
    public bool IsMandatory => true;
    public int Order => 6;

    // Opens the employee list in bulk invitation mode (see EmployeeList.razor's "mode=invite"
    // query parameter) rather than User Administration — the employee list is company-scoped
    // ("/companies/{CompanyId:guid}/employees"), and the "{companyId}" placeholder is substituted
    // by HR.Web with the current company id. OnboardingTaskCard.ResolvedLinkUrl appends its own
    // "returnUrl" query parameter after this, joined with "&" rather than a second "?".
    public Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult("/companies/{companyId}/employees?mode=invite");

    public async Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var employeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(companyId, cancellationToken);

        var activeUserCount = await dbContext.Users
            .AsNoTracking()
            .CountAsync(u => employeeIds.Contains(u.Id) && u.IsActive, cancellationToken);

        if (activeUserCount > 1)
            return true;

        // Bulk-invitation spec, section 7: an admin should not have to wait for an invitee to
        // accept before this checklist step completes. A successfully sent invitation (individual
        // or bulk — both flows set UserInvite.EmailSentAt the same way) to another employee also
        // completes the task. Queuing alone, or a batch where every recipient is skipped/failed,
        // must not complete it — hence checking EmailSentAt rather than mere invite existence.
        return await dbContext.UserInvites
            .AsNoTracking()
            .AnyAsync(
                i => i.CompanyId == companyId && employeeIds.Contains(i.EmployeeId) && i.EmailSentAt != null,
                cancellationToken);
    }
}

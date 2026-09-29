using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Jobs;

internal sealed class ExpireEmployeeRoleOverridesJob(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        var expired = await db.EmployeeRoleOverrides
            .Where(o => o.ExpiresAt != null && o.ExpiresAt <= now)
            .ToListAsync();

        if (expired.Count == 0)
            return;

        db.EmployeeRoleOverrides.RemoveRange(expired);
        await db.SaveChangesAsync();

        foreach (var @override in expired)
        {
            await auditEventPublisher.PublishAsync(
                new EmployeeRoleOverrideExpiredAuditEvent(
                    @override.CompanyId, @override.UserId, @override.Id, @override.RoleId, @override.OverrideType, now),
                CancellationToken.None);
        }
    }
}

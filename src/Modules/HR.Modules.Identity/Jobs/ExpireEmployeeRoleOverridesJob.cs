using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Jobs;

// IAM-04: temporary overrides already stop affecting access the instant they expire (see
// IdentityAuthorizationService.GetEffectiveRolesAsync's ExpiresAt filter) — this job exists purely
// to produce the required audit trail entry and keep the table from accumulating stale rows
// indefinitely. Runs daily; safe to run repeatedly (an override is removed at most once).
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

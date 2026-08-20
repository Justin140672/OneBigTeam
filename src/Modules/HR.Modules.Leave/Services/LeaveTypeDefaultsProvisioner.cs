using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services;

/// <summary>
/// Implements ILeaveTypeDefaultsProvisioner — see the interface doc comment in
/// HR.Infrastructure.Abstractions for why this exists. Mirrors LeaveModule.SeedLeaveAsync's dev
/// seed set (minus Sick Leave, deliberately removed from the default set) so production
/// provisioning never drifts out of sync with what the dev/E2E environment already treats as
/// "correct".
/// </summary>
internal sealed class LeaveTypeDefaultsProvisioner(LeaveDbContext dbContext, IClock clock) : ILeaveTypeDefaultsProvisioner
{
    public async Task EnsureDefaultLeaveTypesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await dbContext.LeaveTypes.AnyAsync(lt => lt.CompanyId == companyId, cancellationToken))
            return;

        var now = clock.UtcNowOffset();

        dbContext.LeaveTypes.AddRange(
            LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now),
            LeaveType.Create(Guid.NewGuid(), companyId, "Unpaid Leave", "UNPAID", 0, AccrualMethod.None, LeaveTypeBehaviour.Unpaid, now, hasBalance: false),
            LeaveType.Create(Guid.NewGuid(), companyId, "Compassionate Leave", "COMPASSIONATE", 5, AccrualMethod.None, LeaveTypeBehaviour.Standard, now),
            LeaveType.Create(Guid.NewGuid(), companyId, "Parental Leave", "PARENTAL", 52, AccrualMethod.None, LeaveTypeBehaviour.Parental, now),
            LeaveType.Create(Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0, AccrualMethod.None, LeaveTypeBehaviour.Toil, now));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

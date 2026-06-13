using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.InitialiseEmployeeLeave;

internal sealed class EmployeeCreatedHandler : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICompanyLeaveSettingsReader _leaveSettingsReader;

    public EmployeeCreatedHandler(LeaveDbContext dbContext, IClock clock, ICompanyLeaveSettingsReader leaveSettingsReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _leaveSettingsReader = leaveSettingsReader;
    }

    public async Task HandleAsync(EmployeeCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var activeLeaveTypes = await _dbContext.LeaveTypes
            .Where(lt => lt.CompanyId == integrationEvent.CompanyId && lt.IsActive)
            .ToListAsync(cancellationToken);

        if (activeLeaveTypes.Count == 0)
            return;

        var assignment = await _dbContext.EmployeeLeavePolicyAssignments
            .FirstOrDefaultAsync(
                a => a.CompanyId == integrationEvent.CompanyId && a.EmployeeId == integrationEvent.EmployeeId,
                cancellationToken);

        if (assignment is null)
            return;

        var leaveSettings = await _leaveSettingsReader.GetLeaveSettingsAsync(integrationEvent.CompanyId, cancellationToken);
        var now = _clock.UtcNowOffset();
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, leaveSettings.LeaveYearStartMonth);

        var balances = activeLeaveTypes.Select(lt => LeaveBalance.Create(
            Guid.NewGuid(),
            integrationEvent.CompanyId,
            integrationEvent.EmployeeId,
            lt.Id,
            assignment.LeavePolicyId,
            policyYear,
            lt.Behaviour == LeaveTypeBehaviour.Toil ? 0 : lt.DefaultEntitlementDays,
            now)).ToList();

        _dbContext.LeaveBalances.AddRange(balances);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

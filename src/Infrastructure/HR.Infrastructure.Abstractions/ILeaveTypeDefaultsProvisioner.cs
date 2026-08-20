namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Sanctioned cross-module contract, implemented internally in HR.Modules.Leave, that seeds a
/// brand-new company's default set of Leave Types (Annual Leave, Unpaid Leave, Compassionate
/// Leave, Parental Leave, Time Off In Lieu — the same canonical set the dev/E2E seed data uses for
/// Acme/Beta Corp, minus Sick Leave, which was deliberately removed from the default set). Added
/// to close a real gap: a company created via self-service signup previously got a default Leave
/// Policy (see ILeavePolicyProvisioner) but no Leave Types at all. Mirrors
/// ILeavePolicyProvisioner's shape/placement exactly. Idempotent — a company that already has any
/// Leave Types is left untouched.
/// </summary>
public interface ILeaveTypeDefaultsProvisioner
{
    Task EnsureDefaultLeaveTypesAsync(Guid companyId, CancellationToken cancellationToken);
}

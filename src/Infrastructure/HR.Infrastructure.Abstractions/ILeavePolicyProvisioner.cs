namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Sanctioned cross-module contract, implemented internally in HR.Modules.Leave, that guarantees a
/// company has at least one default LeavePolicy. Added specifically to unblock
/// ICompanyDefaultDataSeeder (HR.Modules.Employees): PositionProfile.DefaultLeavePolicyId is a
/// required, non-nullable FK into the Leave module's own schema, and no existing code path seeds a
/// LeavePolicy at company-creation time (verified: CreateCompanyHandler in HR.Modules.Companies
/// does not seed one either — this is a genuine pre-existing gap, not something introduced by the
/// self-service signup flow). Without this contract, a brand-new company's bootstrap PositionProfile
/// could never be created. Mirrors the existing ILeavePolicyReader contract shape/placement.
/// </summary>
public interface ILeavePolicyProvisioner
{
    Task<Guid> EnsureDefaultLeavePolicyAsync(Guid companyId, CancellationToken cancellationToken);
}

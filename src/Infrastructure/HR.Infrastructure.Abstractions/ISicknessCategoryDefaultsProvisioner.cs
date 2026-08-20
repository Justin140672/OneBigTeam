namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Sanctioned cross-module contract, implemented internally in HR.Modules.Sickness, that seeds a
/// brand-new company's default set of Sickness Categories (Cold/Flu, Back Pain, Migraine — the
/// same canonical set the dev/E2E seed data uses for Acme). Added to close a real gap: a company
/// created via self-service signup previously got no Sickness Categories at all. Mirrors
/// ILeavePolicyProvisioner's shape/placement exactly. Idempotent — a company that already has any
/// Sickness Categories is left untouched.
/// </summary>
public interface ISicknessCategoryDefaultsProvisioner
{
    Task EnsureDefaultSicknessCategoriesAsync(Guid companyId, CancellationToken cancellationToken);
}

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Sanctioned cross-module contract, implemented internally in HR.Modules.Documents, that seeds a
/// brand-new company's default set of Document Types (Contract, Passport, Driving Licence, Right
/// To Work, Certificate, Other — the same canonical set the dev/E2E seed data uses for Acme/Beta
/// Corp). Added to close a real gap: a company created via self-service signup previously got no
/// Document Types at all. Mirrors ILeavePolicyProvisioner's shape/placement exactly. Idempotent —
/// a company that already has any Document Types is left untouched.
/// </summary>
public interface IDocumentTypeDefaultsProvisioner
{
    Task EnsureDefaultDocumentTypesAsync(Guid companyId, CancellationToken cancellationToken);
}

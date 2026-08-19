namespace HR.Modules.Companies.Contracts;

// Port used by HR.Modules.Identity's self-service SignUp feature to provision a brand-new company
// (plus default settings and a trial subscription) without Identity taking a direct reference to
// HR.Modules.Companies (module boundary rules forbid module-to-module references). Implemented in
// HR.Modules.Companies as CompanyProvisioner, following the same sanctioned cross-module contract
// pattern as IEmployeeProvisioningService.
public interface ICompanyProvisioner
{
    Task<Guid> ProvisionCompanyAsync(string companyName, CancellationToken cancellationToken);

    // Best-effort compensation used by SignUpHandler's failure-cleanup path: if a later step in the
    // signup orchestration fails after the company row already exists, the company is marked
    // Deactivated rather than deleted (avoids FK cleanup complexity — see SignUpHandler's
    // no-cross-module-transaction comment). Not a full saga/outbox pattern — accepted debt.
    Task DeactivateCompanyAsync(Guid companyId, CancellationToken cancellationToken);

    // Used by Identity's VerifyEmail feature (Phase D) to decide, before doing anything else,
    // whether a verification click is a genuine first activation or an idempotent repeat click on
    // an already-active company — repeat clicks must not re-run Company.Activate or re-publish the
    // CompanyActivatedAuditEvent.
    Task<bool> IsCompanyActiveAsync(Guid companyId, CancellationToken cancellationToken);

    // Activates a PendingVerification company. Used by Identity's VerifyEmail feature on first
    // successful verification, and by the dev-only /api/dev/activate-company endpoint (which
    // replaces the removed /api/dev/confirm-email stub) for local testing without live Supabase.
    // Idempotent: calling this on an already-Active company is a harmless no-op (Company.Activate
    // just re-sets the same status).
    Task ActivateCompanyAsync(Guid companyId, CancellationToken cancellationToken);
}

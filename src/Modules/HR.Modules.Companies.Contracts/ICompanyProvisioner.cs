namespace HR.Modules.Companies.Contracts;

public sealed record CompanyProvisioningAdmin(string Email, string FirstName, string LastName);

public interface ICompanyProvisioner
{
    Task<Guid> ProvisionCompanyAsync(string companyName, CompanyProvisioningAdmin admin, CancellationToken cancellationToken);

    Task DeactivateCompanyAsync(Guid companyId, CancellationToken cancellationToken);

    // Used by Identity's VerifyEmail feature (Phase D) to decide, before doing anything else,
    // whether a verification click is a genuine first activation or an idempotent repeat click on
    // an already-active company — repeat clicks must not re-run Company.Activate or re-publish the
    // CompanyActivatedAuditEvent.
    Task<bool> IsCompanyActiveAsync(Guid companyId, CancellationToken cancellationToken);

    Task ActivateCompanyAsync(Guid companyId, CancellationToken cancellationToken);
}

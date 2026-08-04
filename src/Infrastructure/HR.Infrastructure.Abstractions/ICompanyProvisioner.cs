namespace HR.Infrastructure.Abstractions;

// Port used by HR.Modules.Identity's self-service SignUp feature to provision a brand-new company
// (plus default settings and a trial subscription) without Identity taking a direct reference to
// HR.Modules.Companies (module boundary rules forbid module-to-module references). Implemented in
// HR.Modules.Companies as CompanyProvisioner, following the same sanctioned cross-module contract
// pattern as IEmployeeProvisioningService.
public interface ICompanyProvisioner
{
    Task<Guid> ProvisionCompanyAsync(string companyName, CancellationToken cancellationToken);
}

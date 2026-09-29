using FastEndpoints;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel.DevEndpoints;

namespace HR.Modules.Identity.Features.DevActivateCompany;

// Gate: Development environment AND DevTools:Enabled=true, enforced by the shared [DevOnlyEndpoint]
// discovery filter + 404 pre-processor (HR.SharedKernel.DevEndpoints). Anonymous by design (dev tooling).
[DevOnlyEndpoint(DevEndpointKind.DevTools)]
internal sealed class Endpoint(
    ICompanyProvisioner companyProvisioner) : Endpoint<DevActivateCompanyRequest>
{
    public override void Configure()
    {
        Post("/api/dev/activate-company");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DevActivateCompanyRequest request, CancellationToken cancellationToken)
    {
        await companyProvisioner.ActivateCompanyAsync(request.CompanyId, cancellationToken);

        await Send.NoContentAsync(cancellationToken);
    }
}

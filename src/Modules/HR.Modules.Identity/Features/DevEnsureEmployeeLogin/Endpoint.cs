using FastEndpoints;
using HR.SharedKernel.DevEndpoints;

namespace HR.Modules.Identity.Features.DevEnsureEmployeeLogin;

// Gate: Development environment AND DevTools:Enabled=true, enforced by the shared [DevOnlyEndpoint]
// discovery filter + 404 pre-processor (HR.SharedKernel.DevEndpoints). Anonymous by design (dev tooling).
[DevOnlyEndpoint(DevEndpointKind.DevTools)]
internal sealed class Endpoint(
    IServiceProvider serviceProvider) : Endpoint<DevEnsureEmployeeLoginRequest>
{
    public override void Configure()
    {
        Post("/api/dev/ensure-employee-login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DevEnsureEmployeeLoginRequest request, CancellationToken cancellationToken)
    {
        await serviceProvider.EnsureDevSupabaseUserAsync(
            request.EmployeeId, request.CompanyId, request.Email,
            request.FirstName, request.LastName, cancellationToken);

        await Send.NoContentAsync(cancellationToken);
    }
}

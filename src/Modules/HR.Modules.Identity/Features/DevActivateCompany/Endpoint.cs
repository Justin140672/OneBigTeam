using FastEndpoints;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace HR.Modules.Identity.Features.DevActivateCompany;

// Dev-only replacement for the removed /api/dev/confirm-email stub (Features/ConfirmEmail).
// Local dev/demo testing of "what does the app look like once a company is Active" doesn't need a
// real verified Supabase user — the dev-persona switcher (DevAuthHandler/DevPersonaStore in
// HR.Api) already provides a usable local session separately, untouched by this whole epic. This
// endpoint just needs to flip the target company's status directly via the same sanctioned
// ICompanyProvisioner cross-module contract VerifyEmail uses, without any Supabase/identity
// interaction. 404s outside Development, mirroring every other /api/dev/* endpoint.
internal sealed class Endpoint(
    ICompanyProvisioner companyProvisioner,
    IWebHostEnvironment environment) : Endpoint<DevActivateCompanyRequest>
{
    public override void Configure()
    {
        Post("/api/dev/activate-company");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DevActivateCompanyRequest request, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await companyProvisioner.ActivateCompanyAsync(request.CompanyId, cancellationToken);

        await Send.NoContentAsync(cancellationToken);
    }
}
